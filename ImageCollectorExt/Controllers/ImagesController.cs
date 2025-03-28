using ImageCollectorExt.Repository;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using ImageCollectorExt.Models;
using Azure.Storage.Blobs;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision;
using Microsoft.Azure.CognitiveServices.Vision.ComputerVision.Models;
using Microsoft.AspNetCore.Authorization;

namespace ImageCollectorExt.Controllers
{
    [Authorize]
    public class ImagesController : Controller
    {
        private readonly ILogger<ImagesController> _logger;
        private readonly BlobContainerClient _containerClient;
        private readonly ComputerVisionClient _computerVisionClient;
        private readonly AppDbContext _appDbContext;

        public ImagesController(AppDbContext appDbContext, BlobContainerClient containerClient, ComputerVisionClient computerVisionClient, ILogger<ImagesController> logger)
        {
            _containerClient = containerClient;
            _computerVisionClient = computerVisionClient;
            _appDbContext = appDbContext;
            _logger = logger;

            _logger.LogInformation("ImagesController Start");
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Watch()
        {
            var displayCollection = new List<DisplayModel>();

            var fileRecords = _appDbContext.FileRecords.ToList();
            foreach (var fileRecord in fileRecords)
            {
                var fileName = fileRecord.FileName;
                var blobClient = _containerClient.GetBlobClient(fileName);
                var url = blobClient.GenerateSasUri(Azure.Storage.Sas.BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddDays(1));
                displayCollection.Add(new DisplayModel
                {
                    DisplayName = fileName,
                    DisplayUri = url,
                    DisplayContent = fileRecord.Content,
                    DisplayDescription = fileRecord.Description,
                });
            }

            _logger.LogInformation("Images Watch");

            return View(displayCollection);
        }

        public ActionResult Upload()
        {
            return View();
        }

        [Authorize(Policy = "EpamersOnly")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload([FromForm] string description, [FromForm] IFormFile file)
        {
            _logger.LogInformation("Image upload start"+file?.FileName.ToString());

            try
            {
                if (file is null || file.Length == 0)
                {
                    return BadRequest();
                }

                var rec = _appDbContext.FileRecords.Where(r => r.FileName == file.FileName).FirstOrDefault();

                if (rec != null)
                {
                    return BadRequest();
                }

                var lines = await RegognizeStringAsync(file);


                var record = new FileRecord
                {
                    FileName = file.FileName,
                    Content = lines,
                    Description = description,
                };

                _appDbContext.FileRecords.Add(record);
                _appDbContext.SaveChanges();

                _containerClient.UploadBlob(file.FileName, file.OpenReadStream());

                _logger.LogInformation("Image upload finish");
                return RedirectToAction(nameof(Watch));
            }
            catch (Exception ex)
            {
                _logger.LogError("Image upload error" + ex.Message);
                return View();
            }
        }

        public ActionResult Details(string blobName)
        {
            if (blobName is null || blobName.Length == 0)
            {
                return BadRequest();
            }

            var record = _appDbContext.FileRecords.Where(r => r.FileName == blobName).FirstOrDefault();

            if (record is null)
            {
                return BadRequest();
            }

            var blobClient = _containerClient.GetBlobClient(blobName);
            var url = blobClient.GenerateSasUri(Azure.Storage.Sas.BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddDays(1));
            ViewBag.url = url;
            ViewBag.name = record?.FileName;
            ViewBag.content = record?.Content;

            _logger.LogInformation($"Image Details {url}, {record?.FileName}, {record?.Content}");

            return View();
        }

        public ActionResult Download(string blobName)
        {
            if (blobName is null || blobName.Length == 0)
            {
                return BadRequest();
            }

            var record = _appDbContext.FileRecords.Where(r => r.FileName == blobName).FirstOrDefault();

            if (record is null)
            {
                return BadRequest();
            }

            var blobClient = _containerClient.GetBlobClient(blobName);

            var ms = new MemoryStream();
            blobClient.DownloadTo(ms);
            ms.Position = 0;

            _logger.LogInformation($"Image Download {blobName}");

            return File(ms, "application/octet-stream", blobName);
        }

        [Authorize(Policy = "EpamersOnly")]
        [HttpGet]
        public ActionResult Delete(string blobName)
        {
            try
            {
                if (blobName is null || blobName.Length == 0)
                {
                    return BadRequest();
                }

                var record = _appDbContext.FileRecords.Where(r => r.FileName == blobName).FirstOrDefault();

                if (record is null)
                {
                    return BadRequest();
                }

                _appDbContext.FileRecords.Remove(record);
                _appDbContext.SaveChanges();

                var blobClient = _containerClient.GetBlobClient(blobName);

                blobClient.DeleteIfExists();

                _logger.LogInformation($"Image Delete {blobName}");

                return RedirectToAction(nameof(Watch));
            }
            catch (Exception ex)
            {
                _logger.LogError("Image Delete error" + ex.Message);
                return View();
            }
        }


        private async Task<string> RegognizeStringAsync(IFormFile file)
        {
            try
            {
                var stream = file.OpenReadStream();

                ReadInStreamHeaders textHeaders = await _computerVisionClient.ReadInStreamAsync(stream);

                string operationLocation = textHeaders.OperationLocation;
                string operationId = operationLocation[^36..];

                ReadOperationResult results;

                do
                {
                    results = await _computerVisionClient.GetReadResultAsync(Guid.Parse(operationId));
                }
                while ((results.Status == OperationStatusCodes.Running ||
                        results.Status == OperationStatusCodes.NotStarted));

                IList<ReadResult> textUrlFileResults = results.AnalyzeResult.ReadResults;

                StringBuilder sb = new();
                foreach (ReadResult page in textUrlFileResults)
                {
                    foreach (Line line in page.Lines)
                    {
                        sb.AppendLine(line.Text);
                    }
                }

                _logger.LogInformation("Image RegognizeStringAsync finish");
                return string.Join(Environment.NewLine, sb);
            }
            catch (Exception ex)
            {
                _logger.LogError("Image RegognizeStringAsync error" + ex.Message);
                return "";
            }
        }
    }
}
