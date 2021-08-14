using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using ContactCenter.Core.Models;
using ContactCenter.Data;
using ContactCenter.Infrastructure.Utilities;

namespace LandingPage
{
	public class LandingPageService
	{
		private ApplicationDbContext _context;
		private readonly ILogger<LandingPageService> _logger;
		private readonly IConfiguration _configuration;

		public LandingPageService(ApplicationDbContext context,ILogger<LandingPageService> logger, IConfiguration configuration)
		{
			// Crate a new instace of DbContext
			_context = context;
			_logger = logger;
			_configuration = configuration;

		}
		public async Task<string> GetPageHtml(string code)
		{
			string html = string.Empty;

			// Check if we have a code
			if (!string.IsNullOrEmpty(code))
			{
				// Para usar o método da classe
				Message baseMessage = new Message();

				// Mensagem que vamos buscar
				Message message;

				// Confere o tamanho do código
				if ( code.Length >= 5)
                {
					// Quando o código da pagina tiver 5 ou mais caracteres, localiza a smart page direto pelo código
					message = await _context.Messages
								.Where(p => p.SmartCode == code )
								.FirstOrDefaultAsync();
				}
				else
                {
					// Quando o codigo da pagina tiver menos de 5 caracteres, converte no índice e localiza a smart-page pelo indice - necessário porque o banco é case insensitive e os códigos curtos são case sensitive
					message = await _context.Messages
								.Where(p => p.SmartIndex == baseMessage.UnCodeIndex(code))
								.FirstOrDefaultAsync();
				}

				// Se achou
				if ( message != null)
				{
					// Localiza o último envio que usou esta smart-page
					try
					{
						Sending sending = await _context.Sendings
							.Where(p => p.SmartPageId == message.Id)
							.OrderBy(p=>p.Id)
							.LastOrDefaultAsync();

						// Marca o Hit ( visita )
						SmartHit smartHit = new SmartHit { MessageId = message.Id, Time = Utility.HoraLocal(), SendingId = sending?.Id };
						await _context.SmartHits.AddAsync(smartHit);
						await _context.SaveChangesAsync();

					}
					catch ( Exception ex)
					{
						_logger.LogError(ex.Message);
						if (ex.InnerException != null)
							_logger.LogError(ex.InnerException.Message);
					}

					// Pega o codigo HTML da smart-page
					html = message.Html;

				}

			}

			// Valida se deu tudo certo
			if (string.IsNullOrEmpty(html))
				html = "Erro: pagina nao encontrada!";

			return html;
		}
	}
}
