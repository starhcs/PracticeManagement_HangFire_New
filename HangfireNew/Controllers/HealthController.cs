using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace HangfireNew.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class HealthController : ControllerBase
    {

        [Route("Health")]
        [HttpGet]
        public string Health()
        {
            return "The Project is Live!";
        }
    }
}
