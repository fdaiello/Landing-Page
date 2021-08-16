using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http;
using ContactCenter.Core.Models;
using ContactCenter.Data;
using ContactCenter.Infrastructure.Utilities;
using System.Collections.Specialized;
using System.Collections.Generic;

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
				// Mensagem que vamos buscar
				Landing landing = await SearchLanding(code);

				// Se achou
				if ( landing != null)
				{
					try
					{
						// Marca o Hit ( visita ) na landing
						landing.PageViews++;
						_context.Landings.Update(landing);

						// Marca a visita no histórico
						LandingHit landingHit = new LandingHit { LandingId = landing.Id, Time = Utility.HoraLocal(), HitType=LandingHitType.get };
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
		public async Task<string> PostLanding(string code, HttpRequest httpRequest)
		{
			string html = string.Empty;

			// Check if we have a code
			if (!string.IsNullOrEmpty(code))
			{
				// Mensagem que vamos buscar
				Landing landing = await SearchLanding(code);

				// Se achou
				if (landing != null)
				{
					try
					{
						// Marca o post na landing
						landing.Leads ++;
						_context.Landings.Update(landing);

						// Salva o Lead
						await SaveLead(landing, httpRequest);

						// Marca a visita no histórico
						LandingHit landingHit = new LandingHit { LandingId = landing.Id, Time = Utility.HoraLocal(), HitType = LandingHitType.post };
						await _context.LandingHits.AddAsync(landingHit);
						await _context.SaveChangesAsync();


					}
					catch (Exception ex)
					{
						_logger.LogError(ex.Message);
						if (ex.InnerException != null)
							_logger.LogError(ex.InnerException.Message);
					}

					// Pega o codigo HTML da smart-page
					html = landing.Html;

					// Adiciona a mensagem 
					html = AddMessageToHtml(html);
				}

			}

			// Valida se deu tudo certo
			if (string.IsNullOrEmpty(html))
				html = "Erro: pagina nao encontrada!";

			return html;
		}


		private async Task<Landing> SearchLanding(string code)
		{
			// Para usar o método da classe
			Landing baseLanding = new Landing();

			// Mensagem que vamos buscar
			Landing landing;

			// Confere o tamanho do código
			if (code.Length >= 5)
			{
				// Quando o código da pagina tiver 5 ou mais caracteres, localiza a smart page direto pelo código
				landing = await _context.Landings
							.Where(p => p.Code == code)
							.FirstOrDefaultAsync();
			}
			else
			{
				// Quando o codigo da pagina tiver menos de 5 caracteres, converte no índice e localiza a smart-page pelo indice - necessário porque o banco é case insensitive e os códigos curtos são case sensitive
				landing = await _context.Landings
							.Where(p => p.Index == baseLanding.UnCodeIndex(code))
							.FirstOrDefaultAsync();
			}

			return landing;
		}

		private async Task SaveLead(Landing landing, HttpRequest httpRequest)
		{

			// Cria um novo contato
			Contact contact = new Contact { Id = landing.GroupId + "-" + System.Guid.NewGuid().ToString(), GroupId = landing.GroupId, ChannelType = ChannelType.other, FirstActivity = Utility.HoraLocal(), LastActivity = Utility.HoraLocal(), LastText = "Novo lead" };

			// Busca as propriedades basicas ( nome, email, celular ) do contato que estiverem no form
			GetContactProperties(contact, httpRequest);

			// Busca campos customizados do contato
			await GetContactFieldValues(contact, httpRequest);

			// Salva o lead
			await _context.Contacts.AddAsync(contact);
			await _context.SaveChangesAsync();

			// Busca o primeiro estágio da lista
			Stage stage = await _context.Stages
							.Where(p => p.BoardId == landing.BoardId)
							.OrderBy(p => p.Order)
							.FirstOrDefaultAsync();

			// Se não achou nenhum estágio na lista
			if ( stage == null)
			{
				// Adiciona um estágio a lista
				stage = new Stage { BoardId = landing.BoardId??0, Name = string.Empty, Label = string.Empty };
				await _context.Stages.AddAsync(stage);
				await _context.SaveChangesAsync();
			}

			// Cria um novo cartão ( insere o contato dentro de uma lista )
			Card card = new Card { StageId = stage.Id, CreatedDate = Utility.HoraLocal(), UpdatedDate = Utility.HoraLocal(), ContactId = contact.Id };
			await _context.Cards.AddAsync(card);
			await _context.SaveChangesAsync();

			// Busca se tem campos customizados do cartão
			await GetCardFieldValues(contact, httpRequest, card, landing);

		}
		private void GetContactProperties(Contact contact, HttpRequest httpRequest)
		{
			var form = httpRequest.Form;

			var keys = form.Keys;
			foreach ( var key in keys)
			{
				if (key.ToLower() == "name" || key.ToLower() == "nome")
				{
					contact.Name = form[key.ToString()];
					contact.FullName = contact.Name;
				}
				else if (key.ToLower() == "email" || key.ToLower() == "e-mail")
				{
					contact.Email = form[key.ToString()];
				}
				else if (key.ToLower() == "phone_number" || key.ToLower() == "telefone" || key.ToLower() == "celular")
				{
					contact.MobilePhone = form[key.ToString()];
				}
			}
		}
		private async Task GetContactFieldValues(Contact contact, HttpRequest httpRequest)
		{
			// Busca os campos personalizados que estão ativos para os contatos
			List<ContactField> contactFields = await _context.ContactFields
								.Where(p => p.GroupId == contact.GroupId & p.Enabled)
								.Include(p=> p.Field)
								.ToListAsync();

			var form = httpRequest.Form;
			var keys = form.Keys;
			foreach (var key in keys)
			{
				// Procura se o form tem um dos campos personalizados
				if (contactFields.Where(p => p.Field.Label.ToLower() == key.ToLower()).Any())
				{
					// Adiciona um Contact Field Value
					ContactFieldValue contactFieldValue = new ContactFieldValue { ContactId = contact.Id, FieldId = contactFields.Where(p => p.Field.Label.ToLower() == key.ToLower()).FirstOrDefault().Field.Id, Value = form[key] };
					_context.ContactFieldValues.Add(contactFieldValue);
				}
			}

			// Salva no banco;
			await _context.SaveChangesAsync();
		}
		private async Task GetCardFieldValues(Contact contact, HttpRequest httpRequest, Card card, Landing landing)
		{
			// Busca os campos personalizados que estão ativos para a lista ( board -> boardFields )
			List<BoardField> boardFields = await _context.BoardFields
								.Where(p => p.Board.GroupId == contact.GroupId && p.BoardId==landing.BoardId & p.Enabled)
								.Include(p => p.Field)
								.ToListAsync();

			var form = httpRequest.Form;
			var keys = form.Keys;
			foreach (var key in keys)
			{
				// Procura se o form tem um dos campos personalizados da lista
				if (boardFields.Where(p => p.Field.Label.ToLower() == key.ToLower()).Any())
				{
					// Adiciona um  Card Field Value
					CardFieldValue cardFieldValue = new CardFieldValue { CardId = card.Id, FieldId = boardFields.Where(p => p.Field.Label.ToLower() == key.ToLower()).FirstOrDefault().FieldId, Value = form[key] };
					_context.CardFieldValues.Add(cardFieldValue);
				}
			}
		}
		private string AddMessageToHtml ( string html)
		{
			const string MESSAGE = "<script>alert('Seus dados foram recebidos. Obrigado!')</script>";
			html = html.Replace("</body>", MESSAGE + "</body>");

			return html;
		}
	}
}
