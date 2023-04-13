using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BAMCIS.GeoJSON;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using UNISharedModels.Request;
using UNISharedModels.Response;

namespace DownloadService.Controllers
{
    [ApiController]
    [Route("v1/[controller]")]
    public class GeoXactController : ControllerBase
    {
        private readonly ILogger<GeoXactController> _logger;
        private readonly IRequestClient<RequestRetrieveGeoXact> _clientExport;

        public GeoXactController(ILogger<GeoXactController> logger, IRequestClient<RequestRetrieveGeoXact> clientExport)
        {
            _logger = logger;
            _clientExport = clientExport;

        }

        [HttpGet("{geoxactId}")]
        [ResponseCache(Duration = 31536000)]
        public async Task<ActionResult> GetGeoXact(string geoxactId)
        {
            try
            {
                var entity = await _clientExport.GetResponse<ResponseRetrieveGeoXact>(new { GeoXactId = geoxactId }, timeout: RequestTimeout.After(m: 5)).ConfigureAwait(false);
                return new OkObjectResult(entity.Message.GeoXactJson);
            }
            catch (Exception exception)
            {
                return Problem(detail: exception.Message, statusCode: 500);
            }
        }
    }
}
