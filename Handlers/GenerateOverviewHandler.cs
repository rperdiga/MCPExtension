using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.DomainModels;
using Mendix.StudioPro.ExtensionsAPI.Model.Pages;
using Mendix.StudioPro.ExtensionsAPI.Services;
using MCPExtension.Core;
using System.Text.Json;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System;
using System.ComponentModel.Composition;

namespace MCPExtension.Handlers.Schema
{
    public class OverviewPagesRequest
    {
        public List<string> EntityNames { get; set; } = new List<string>();
        public bool GenerateIndexSnippet { get; set; } = true;
    }
}

namespace MCPExtension.Handlers
{
    public class GenerateOverviewHandler : BaseApiHandler
    {
        private readonly IPageGenerationService pageGenerationService;
        private readonly INavigationManagerService navigationManagerService;

        // Standardize path for MCP compatibility
        public override string Path => "/api/pages/generate-overview";
        public override string Method => "POST";

        [ImportingConstructor]
        public GenerateOverviewHandler(
            IModel currentApp,
            IPageGenerationService pageGenerationService,
            INavigationManagerService navigationManagerService) : base(currentApp)
        {
            this.pageGenerationService = pageGenerationService;
            this.navigationManagerService = navigationManagerService;
        }

        public override async Task HandleAsync(HttpContext context)
        {
            await ExecuteInTransactionAsync(
                context,
                "generate overview pages",
                async (model) =>
                {
                    try
                    {
                        var request = await DeserializeRequestBodyAsync<Schema.OverviewPagesRequest>(context);

                        if (request == null || request.EntityNames == null || !request.EntityNames.Any())
                        {
                            return (
                                success: false,
                                message: "Invalid request format or no entity names provided",
                                data: null as object
                            );
                        }

                        var module = Utils.Utils.GetMyFirstModule(model);
                        if (module?.DomainModel == null)
                        {
                            return (
                                success: false,
                                message: "No domain model found.",
                                data: null as object
                            );
                        }

                        // Get all entities from the domain model
                        var allEntities = module.DomainModel.GetEntities().ToList();
                        
                        // Filter entities based on the requested names
                        var entitiesToGenerate = allEntities
                            .Where(e => request.EntityNames.Contains(e.Name, StringComparer.OrdinalIgnoreCase))
                            .ToList();

                        if (!entitiesToGenerate.Any())
                        {
                            return (
                                success: false,
                                message: "None of the requested entities were found in the domain model",
                                data: null as object
                            );
                        }

                        // Generate overview pages using the injected service
                        var generatedOverviewPages = pageGenerationService.GenerateOverviewPages(
                            module,
                            entitiesToGenerate,
                            request.GenerateIndexSnippet
                        );

                        // Add pages to navigation using the injected service
                        var overviewPages = generatedOverviewPages
                            .Where(page => page.Name.Contains("overview", StringComparison.InvariantCultureIgnoreCase))
                            .Select(page => (page.Name, page))
                            .ToArray();

                        navigationManagerService.PopulateWebNavigationWith(
                            model,
                            overviewPages
                        );

                        return (
                            success: true,
                            message: $"Successfully generated {overviewPages.Length} overview pages",
                            data: new
                            {
                                GeneratedPages = overviewPages.Select(p => p.Name).ToList()
                            }
                        );
                    }
                    catch (Exception ex)
                    {
                        return (
                            success: false,
                            message: $"Error generating overview pages: {ex.Message}",
                            data: null as object
                        );
                    }
                });
        }

        private async Task<T?> DeserializeRequestBodyAsync<T>(HttpContext context) where T : class
        {
            string requestBody;
            using (var reader = new StreamReader(context.Request.Body))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            return JsonSerializer.Deserialize<T>(requestBody, options);
        }
    }
}
