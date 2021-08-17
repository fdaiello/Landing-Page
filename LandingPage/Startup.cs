using System;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ContactCenter.Data;
using ContactCenter.Infrastructure.Clients.MailService;

[assembly: FunctionsStartup(typeof(LandingPage.Startup))]

namespace LandingPage
{
	public class Startup : FunctionsStartup
	{

		// Loads Configuration from Azure Configuration Store
		public override void ConfigureAppConfiguration(IFunctionsConfigurationBuilder builder)
		{
			// Load configuration from Azure Configuration Store
			string cs = Environment.GetEnvironmentVariable("AppConfigurationConnectionString");
			builder.ConfigurationBuilder.AddAzureAppConfiguration(cs);
		}

		public override void Configure(IFunctionsHostBuilder builder)
		{

			// String de conexão
			string connectionString = Environment.GetEnvironmentVariable("DefaultConnection", EnvironmentVariableTarget.Process);

			// Database
			builder.Services.AddDbContext<ApplicationDbContext>(options =>
				options.UseSqlServer(connectionString));

			// Smart Page Service
			builder.Services.AddTransient<LandingPageService>();

			// Get Configuration
			var configuration = builder.GetContext().Configuration;
			builder.Services.AddSingleton(configuration);

			// Email Service
			builder.Services.AddTransient<MailService>();

		}

	}
}