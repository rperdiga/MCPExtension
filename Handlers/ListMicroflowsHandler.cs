using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Microflows;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using MCPExtension.Core;
using System.Linq;
using System.Text.Json;

namespace MCPExtension.Handlers
{
    public class ListMicroflowsHandler : BaseApiHandler
    {
        public override string Path => "/api/microflows/list";
        public override string Method => "POST";

        public ListMicroflowsHandler(IModel currentApp) : base(currentApp) { }

        public override async Task HandleAsync(HttpContext context)
        {
            try
            {
                var requestBody = await JsonSerializer.DeserializeAsync<RequestBody>(
                    context.Request.Body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );
                if (requestBody == null || string.IsNullOrEmpty(requestBody.ModuleName))
                {
                    context.Response.StatusCode = 400;
                    await WriteJsonResponse(context, new { success = false, message = "Module name is required." });
                    return;
                }

                var module = CurrentApp.Root.GetModules().FirstOrDefault(m => m.Name == requestBody.ModuleName);
                if (module == null)
                {
                    context.Response.StatusCode = 404;
                    await WriteJsonResponse(context, new { success = false, message = "Module not found." });
                    return;
                }

                var microflows = module.GetDocuments()
                    .OfType<IMicroflow>()
                    .Select(mf => new
                    {
                        mf.Name,
                        QualifiedName = mf.QualifiedName?.FullName
                    }).ToList();

                await WriteJsonResponse(context, new
                {
                    success = true,
                    message = "Microflows retrieved successfully.",
                    data = microflows
                });
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                await WriteJsonResponse(context, new
                {
                    success = false,
                    message = $"Error retrieving microflows: {ex.Message}"
                });
            }
        }

        private async Task WriteJsonResponse(HttpContext context, object value)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(value, options));
        }

        private class RequestBody
        {
            public string? ModuleName { get; set; }
        }
    }
}