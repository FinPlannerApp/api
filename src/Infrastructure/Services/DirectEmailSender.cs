using Application.DTOs.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Infrastructure.Services;

public interface IDirectEmailSender
{
    Task SendEmailAsync(MailRequest mailRequest);
}

public class DirectEmailSender : IDirectEmailSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DirectEmailSender> _logger;
    private readonly HttpClient _httpClient;

    public DirectEmailSender(IConfiguration configuration, ILogger<DirectEmailSender> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _httpClient = new HttpClient();
    }

    public async Task SendEmailAsync(MailRequest mailRequest)
    {
        try
        {
            // 1. Always log for development
            _logger.LogInformation("[DirectEmail] To {To}: {Subject}\n--- BODY ---\n{Body}\n--- END BODY ---", 
                mailRequest.To, mailRequest.Subject, mailRequest.Body);
            
            // 2. Try to use Brevo API
            var apiKey = _configuration["Brevo:ApiKey"];
            
            if (!string.IsNullOrEmpty(apiKey) && apiKey != "YOUR_BREVO_API_KEY")
            {
                var senderName = _configuration["Brevo:SenderName"] ?? "Financial Planner App";
                var senderEmail = _configuration["Brevo:SenderEmail"] ?? "no-reply@localhost";

                var brevoPayload = new
                {
                    sender = new { name = senderName, email = senderEmail },
                    to = new[] { new { email = mailRequest.To } },
                    subject = mailRequest.Subject,
                    htmlContent = mailRequest.Body
                };

                var requestContent = new StringContent(JsonSerializer.Serialize(brevoPayload), Encoding.UTF8, "application/json");
                
                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
                requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                requestMessage.Headers.Add("api-key", apiKey);
                requestMessage.Content = requestContent;

                var response = await _httpClient.SendAsync(requestMessage);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("[DirectEmail] Sent successfully to {To} via Brevo API.", mailRequest.To);
                }
                else
                {
                    var errorResponse = await response.Content.ReadAsStringAsync();
                    _logger.LogError("[DirectEmail] Brevo API failed. Status: {StatusCode}, Error: {Error}", response.StatusCode, errorResponse);
                }
            }
            else
            {
                _logger.LogWarning("[DirectEmail] No valid Brevo API key. Email simulated.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DirectEmail] Failed to send email to {To}.", mailRequest.To);
        }
    }
}
