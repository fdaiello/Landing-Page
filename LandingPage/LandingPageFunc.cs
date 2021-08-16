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

        [FunctionName("LandingPageRequest")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "{code}")] HttpRequest req, string code, ILogger log)
        {
            log.LogInformation($"Landing page function processed an http request. Code: {code}");
            string content = string.Empty;

            //// Chamada adicional do browser - vamos ignorar
            if (code == "favicon.ico")
                return new NotFoundResult();

            // Confere o método
            if ( req.Method == HttpMethods.Get )
                // Chama o serviço da Landing - get
                content = await _landingPageService.GetLanding(code);

            else if ( req.Method == HttpMethods.Post)
                // Chama o serviço da Landing - post
                content = await _landingPageService.PostLanding(code, req);

            // Devolve o conteudo
            return new ContentResult { Content = content, ContentType = "text/html" };
        }

        /*
         * Binding is not working !!!
         */
        [FunctionName("LandingPageVoidRequest")]
        public IActionResult VoidRequest(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "")] HttpRequest req, ILogger log)
        {
            log.LogInformation($"Landing page function with code processed an http request. Code: NULL");

            // Redirect
            return new RedirectResult("https://www.misterpostman.com.br", true);
        }
    }
}
