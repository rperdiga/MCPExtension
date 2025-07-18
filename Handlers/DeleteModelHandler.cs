using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.DomainModels;
using MCPExtension.Core;
using System.Text.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MCPExtension.Handlers
{
    public class DeleteRequest
    {
        public string Type { get; set; } = string.Empty; // "entity", "attribute", or "association"
        public string EntityName { get; set; } = string.Empty;
        public string AttributeName { get; set; } = string.Empty;
        public string AssociationName { get; set; } = string.Empty;
    }

    public class DeleteModelHandler : BaseApiHandler
    {
        // Standardize path for MCP compatibility
        public override string Path => "/api/model/delete";
        public override string Method => "POST"; // Changed from DELETE to POST for MCP compatibility

        public DeleteModelHandler(IModel currentApp) : base(currentApp) { }

        public override async Task HandleAsync(HttpContext context)
        {
            await ExecuteInTransactionAsync(
                context,
                "delete model element",
                async (model) =>
                {
                    try
                    {
                        var request = await DeserializeRequestBodyAsync<DeleteRequest>(context);

                        if (request == null)
                        {
                            return (
                                success: false,
                                message: "Invalid request format",
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

                        switch (request.Type.ToLower())
                        {
                            case "entity":
                                return DeleteEntity(module.DomainModel, request.EntityName);
                            
                            case "attribute":
                                return DeleteAttribute(module.DomainModel, request.EntityName, request.AttributeName);
                            
                            case "association":
                                return DeleteAssociation(module.DomainModel, request.EntityName, request.AssociationName);
                            
                            default:
                                return (
                                    success: false,
                                    message: $"Unknown deletion type: {request.Type}",
                                    data: null as object
                                );
                        }
                    }
                    catch (Exception ex)
                    {
                        return (
                            success: false,
                            message: $"Error during deletion: {ex.Message}",
                            data: null as object
                        );
                    }
                });
        }

        private (bool success, string message, object? data) DeleteEntity(IDomainModel domainModel, string entityName)
        {
            using (var transaction = CurrentApp.StartTransaction("Delete Entity"))
            {
                var entity = domainModel.GetEntities().FirstOrDefault(e => e.Name == entityName);
                if (entity == null)
                {
                    return (
                        success: false,
                        message: $"Entity '{entityName}' not found",
                        data: null as object
                    );
                }

                // Delete all associations first
                var entityAssociations = entity.GetAssociations(AssociationDirection.Both, null).ToList();
                foreach (var entityAssociation in entityAssociations)
                {
                    var association = entityAssociation.Association;
                    entity.DeleteAssociation(association);
                }

                domainModel.RemoveEntity(entity);
                transaction.Commit();

                return (
                    success: true,
                    message: $"Entity '{entityName}' and its associations deleted successfully",
                    data: null as object
                );
            }
        }

        private (bool success, string message, object? data) DeleteAttribute(
            IDomainModel domainModel, 
            string entityName, 
            string attributeName)
        {
            using (var transaction = CurrentApp.StartTransaction("Delete Attribute"))
            {
                var entity = domainModel.GetEntities().FirstOrDefault(e => e.Name == entityName);
                if (entity == null)
                {
                    return (
                        success: false,
                        message: $"Entity '{entityName}' not found",
                        data: null as object
                    );
                }

                var attribute = entity.GetAttributes().FirstOrDefault(a => a.Name == attributeName);
                if (attribute == null)
                {
                    return (
                        success: false,
                        message: $"Attribute '{attributeName}' not found in entity '{entityName}'",
                        data: null as object
                    );
                }

                entity.RemoveAttribute(attribute);
                transaction.Commit();

                return (
                    success: true,
                    message: $"Attribute '{attributeName}' deleted successfully from entity '{entityName}'",
                    data: null as object
                );
            }
        }

        private (bool success, string message, object? data) DeleteAssociation(
            IDomainModel domainModel, 
            string entityName, 
            string associationName)
        {
            using (var transaction = CurrentApp.StartTransaction("Delete Association"))
            {
                var entity = domainModel.GetEntities().FirstOrDefault(e => e.Name == entityName);
                if (entity == null)
                {
                    return (
                        success: false,
                        message: $"Entity '{entityName}' not found",
                        data: null as object
                    );
                }

                var entityAssociation = entity.GetAssociations(AssociationDirection.Both, null)
                    .FirstOrDefault(a => a.Association.Name == associationName);
                if (entityAssociation == null)
                {
                    return (
                        success: false,
                        message: $"Association '{associationName}' not found",
                        data: null as object
                    );
                }

                var association = entityAssociation.Association;
                entity.DeleteAssociation(association);
                transaction.Commit();

                return (
                    success: true,
                    message: $"Association '{associationName}' deleted successfully",
                    data: null as object
                );
            }
        }
    }
}
