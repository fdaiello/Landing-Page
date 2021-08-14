using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ContactCenter.Core.Models;
using ContactCenter.Data;
using ContactCenter.Infrastructure.Utilities;

namespace LandingPage
{
	public class LandingPageService
	{
		private ApplicationDbContext _context;
		private readonly ILogger<LandingPageService> _logger;

		public LandingPageService(ApplicationDbContext context,ILogger<LandingPageService> logger)
		{
			// Crate a new instace of DbContext
			_context = context;
			_logger = logger;

		}
		public async Task<string> GetLanding(string code)
		{
			string html = string.Empty;

			// Check if we have a code
			if (!string.IsNullOrEmpty(code))
			{
				// Para usar o método da classe
				Landing baseLanding = new Landing();

				// Mensagem que vamos buscar
				Landing landing;

				// Confere o tamanho do código
				if ( code.Length >= 5)
                {
					// Quando o código da pagina tiver 5 ou mais caracteres, localiza a smart page direto pelo código
					landing = await _context.Landings
								.Where(p => p.Code == code )
								.FirstOrDefaultAsync();
				}
				else
                {
					// Quando o codigo da pagina tiver menos de 5 caracteres, converte no índice e localiza a smart-page pelo indice - necessário porque o banco é case insensitive e os códigos curtos são case sensitive
					landing = await _context.Landings
								.Where(p => p.Index == baseLanding.UnCodeIndex(code))
								.FirstOrDefaultAsync();
				}

				// Se achou
				if ( landing != null)
				{
					// Localiza o último envio que usou esta smart-page
					try
					{

						// Marca o Hit ( visita ) na landing
						landing.PageViews++;
						_context.Landings.Update(landing);

						// Marca a visita no histórico
						LandingHit landingHit = new LandingHit { LandingId = landing.Id, Time = Utility.HoraLocal() };
						await _context.LandingHits.AddAsync(landingHit);
						await _context.SaveChangesAsync();

					}
					catch ( Exception ex)
					{
						_logger.LogError(ex.Message);
						if (ex.InnerException != null)
							_logger.LogError(ex.InnerException.Message);
					}

					// Pega o codigo HTML da smart-page
					html = landing.Html;

				}

			}

			// Valida se deu tudo certo
			if (string.IsNullOrEmpty(html))
				html = "Erro: pagina nao encontrada!";

			return html;
		}
	}
}
