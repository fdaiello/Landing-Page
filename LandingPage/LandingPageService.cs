using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using ContactCenter.Core.Models;
using ContactCenter.Data;
using ContactCenter.Infrastructure.Utilities;
using System.Collections.Generic;
using ContactCenter.Infrastructure.Clients.MailService;
using System.Net.Mail;

namespace LandingPage
{
	public class LandingPageService
	{
		private ApplicationDbContext _context;
		private readonly ILogger<LandingPageService> _logger;
		private readonly MailService _mailService;
		private readonly IConfiguration _configuration;

		public LandingPageService(ApplicationDbContext context,ILogger<LandingPageService> logger, MailService mailService, IConfiguration configuration)
		{
			// Injected
			_context = context;
			_logger = logger;
			_mailService = mailService;
			_configuration = configuration;

		}
		public async Task<string> GetLanding(string code)
		{
            string content;

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
					content = landing.Html;

					if (string.IsNullOrEmpty(content))
						content = "Pagina em construcao!";
				}
				else
				{
					content = "Erro: pagina nao encontrada!";
				}

			}
			else
            {
				content = "Erro: pagina nao encontrada!";
			}

			return content;
		}
		public async Task<string> PostLanding(string code, HttpRequest httpRequest)
		{
			string content = string.Empty;

			// Check if we have a code
			if (!string.IsNullOrEmpty(code))
			{
				// Mensagem que vamos buscar
				Landing landing = await SearchLanding(code);

				// Se achou
				if (landing != null)
				{
					// Corpo da mensagem de aviso que será enviada por email
					MailMessage mailMessage = new MailMessage { Subject = "Novo Lead", Body = $"<h1>Novo Lead</h1><h2>{landing.Title}</h2>" };

					try
					{
						// Marca o post na landing
						landing.Leads ++;
						_context.Landings.Update(landing);

						// Salva o Lead
						await SaveLead(landing, httpRequest, mailMessage);

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

					// Verifica se tem URL de redirecionamento
					if (landing.RedirUri != null)
                    {
						content = landing.RedirUri.ToString();
                    }
					else
                    {
						// Pega o codigo HTML da smart-page
						content = landing.Html;

						// Adiciona a mensagem 
						content = AddMessageToHtml(content);
					}

					// Verifica se tem email para receber notificação
					if (!string.IsNullOrEmpty(landing.EmailAlert))
					{
						SendMail(landing.EmailAlert, mailMessage );
					}
				}

			}

			// Valida se deu tudo certo
			if (string.IsNullOrEmpty(content))
				content = "Erro: pagina nao encontrada!";

			return content;
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

		private async Task SaveLead(Landing landing, HttpRequest httpRequest, MailMessage mailMessage)
		{
			// Last Text
			string lastText = "Novo Lead";

			// Cria um novo contato
			Contact contact = new Contact { Id = landing.GroupId + "-" + System.Guid.NewGuid().ToString(), GroupId = landing.GroupId, ChannelType = ChannelType.other, FirstActivity = Utility.HoraLocal(), LastActivity = Utility.HoraLocal(), LastText = lastText };

			// Busca as propriedades basicas ( nome, email, celular ) do contato que estiverem no form
			GetContactProperties(contact, httpRequest, mailMessage);

			// Revisa se o contato já não foi incluido ( para evitar duplicar se clicar 2 vezes )
			if ( !_context.Contacts
				.Where(p=>p.LastText==lastText && p.Name==contact.Name && p.MobilePhone==contact.MobilePhone && p.Email==contact.Email && p.FirstActivity > Utility.HoraLocal().Subtract(new TimeSpan(0,0,5,0))).Any())
			{
				// Salva o lead
				await _context.Contacts.AddAsync(contact);
				await _context.SaveChangesAsync();

				// Busca campos customizados do contato
				await GetContactFieldValues(contact, httpRequest, mailMessage);

				// Busca o primeiro estágio da lista
				Stage stage = await _context.Stages
								.Where(p => p.BoardId == landing.BoardId)
								.OrderBy(p => p.Order)
								.FirstOrDefaultAsync();

				// Se não achou nenhum estágio na lista
				if (stage == null)
				{
					// Adiciona um estágio a lista
					stage = new Stage { BoardId = landing.BoardId ?? 0, Name = string.Empty, Label = string.Empty };
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
		}
		private void GetContactProperties(Contact contact, HttpRequest httpRequest, MailMessage mailMessage)
		{
			var form = httpRequest.Form;

			var keys = form.Keys;
			foreach ( var key in keys)
			{
				if (key.ToLower() == "name" || key.ToLower() == "nome")
				{
					contact.Name = form[key.ToString()];
					contact.FullName = contact.Name;
					mailMessage.Body += "Nome: " + contact.Name + "<br>";
				}
				else if (key.ToLower() == "email" || key.ToLower() == "e-mail")
				{
					contact.Email = form[key.ToString()];
					mailMessage.Body += "Email: " + contact.Email + "<br>";
				}
				else if (key.ToLower() == "phone_number" || key.ToLower() == "telefone" || key.ToLower() == "celular")
				{
					contact.MobilePhone = form[key.ToString()];
					mailMessage.Body += "Celular: " + contact.MobilePhone + "<br>";
				}
			}
		}
		private async Task GetContactFieldValues(Contact contact, HttpRequest httpRequest, MailMessage mailMessage)
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

					// Adiciona o campo no corpo da mensagem que será enviada por email
					mailMessage.Body += key + ": " + contactFieldValue.Value + "<br>";
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

		private void SendMail(string recipientEmail, MailMessage mailMessage)
        {

			// Configurações do Host de SMTP
			string senderEmail =  _configuration.GetValue<string>("senderEmail");
			string smtpHost = _configuration.GetValue<string>("smtpHost");
			string smtpLogin = _configuration.GetValue<string>("smtpLogin");
			string smtpPass = _configuration.GetValue<string>("smtpPass");

			// Configura os objetos com os endereços de email 
			MailAddress sender = new MailAddress(senderEmail, senderEmail);
			MailAddress recipient = new MailAddress(recipientEmail, recipientEmail);

			// Configurações do SMTP
			SmtpSettings smtpSettings = new SmtpSettings
			{
				SmtpHost = smtpHost,
				SmtpPort = 587,
				SmtpLogin = smtpLogin,
				SmtpPass = smtpPass,
				EnableSsl = true
			};

            try
            {
				// Envia e obtem um Id da mensagem ( ou string empty se deu erro )
				_mailService.SendMail(sender, recipient, sender, mailMessage.Subject, mailMessage.Body, null, smtpSettings);
			}
			catch( Exception ex)
            {
				_logger.LogError(ex.Message);
				if (ex.InnerException != null)
					_logger.LogError(ex.InnerException.Message);
            }
		}
	}
	public class MailMessage
	{
		public string Subject { get; set; }
		public string Body { get; set; }
	}
}
