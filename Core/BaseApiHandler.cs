using Microsoft.AspNetCore.Http;
using System;
using System.Threading.Tasks;
using Mendix.StudioPro.ExtensionsAPI.Model;
using System.Text.Json;
using System.Text;
using System.IO;

namespace MCPExtension.Core
{
    public abstract class BaseApiHandler : IApiHandler
    {
        public abstract string Path { get; }
        public virtual string Method => "POST"; // Default to POST for MCP compatibility

        protected readonly IModel CurrentApp;
        protected readonly JsonSerializerOptions JsonOptions;

        public BaseApiHandler(IModel currentApp)
        {
            CurrentApp = currentApp;
            JsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }

        public abstract Task HandleAsync(HttpContext context);

        // Helper method for executing operations in a transaction with standardized response handling
        protected async Task ExecuteInTransactionAsync<T>(
            HttpContext context,
            string operationName,
            Func<IModel, Task<(bool success, string message, T? data)>> operation)
        {
            try
            {
                // Set CORS headers safely using TryAdd instead of Add
                context.Response.Headers.TryAdd("Access-Control-Allow-Origin", "*");
                context.Response.Headers.TryAdd("Access-Control-Allow-Methods", "POST, GET, OPTIONS, DELETE");
                context.Response.Headers.TryAdd("Access-Control-Allow-Headers", "Content-Type");

                // Handle OPTIONS requests for CORS preflight
                if (context.Request.Method == "OPTIONS")
                {
                    context.Response.StatusCode = 200;
                    return;
                }

                // Check if the request method matches what's expected
                if (context.Request.Method != Method)
                {
                    context.Response.StatusCode = 405;
                    await WriteJsonResponseAsync(context, new
                    {
                        success = false,
                        message = $"Method not allowed. Use {Method}.",
                        error = "MethodNotAllowed"
                    });
                    return;
                }

                // Execute operation in transaction
                var (success, message, data) = await operation(CurrentApp);
                
                context.Response.StatusCode = success ? 200 : 400;
                await WriteJsonResponseAsync(context, new
                {
                    success,
                    message,
                    data
                });
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                await WriteJsonResponseAsync(context, new
                {
                    success = false,
                    message = $"Error during {operationName}: {ex.Message}",
                    error = "InternalServerError",
                    details = ex.ToString()
                });
            }
        }

        // Helper to read request body as string
        protected async Task<string> ReadRequestBodyAsync(HttpContext context)
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }

        // Helper to deserialize request body to a specific type
        protected async Task<T?> DeserializeRequestBodyAsync<T>(HttpContext context)
        {
            var requestBody = await ReadRequestBodyAsync(context);
            
            if (string.IsNullOrWhiteSpace(requestBody))
                return default;

            return JsonSerializer.Deserialize<T>(requestBody, JsonOptions);
        }

        // Helper to write JSON response
        private async Task WriteJsonResponseAsync(HttpContext context, object value)
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(value, JsonOptions));
        }
    }
}
