using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LandingPage
{

    public class LandingPageFunc
    {

        private readonly LandingPageService _landingPageService;

        public LandingPageFunc(LandingPageService landingPageService)
        {
            _landingPageService = landingPageService;
        }

        [FunctionName("LandingPageGet")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "{code}")] HttpRequest req, string code, ILogger log)
        {
            log.LogInformation($"Landing page function processed an http request. Code: {code}");

            // Chama o serviço da Landing
            string content = await _landingPageService.GetLanding(code);

            // Devolve o conteudo
            return new ContentResult { Content = content, ContentType = "text/html" };
        }
    }
}
