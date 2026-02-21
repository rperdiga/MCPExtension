using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Mendix.StudioPro.ExtensionsAPI.Model.DomainModels;
using Mendix.StudioPro.ExtensionsAPI.Model.DataTypes;
using System.Collections.Generic;

namespace MCPExtension.Utils;

public class Utils
{
    /// <summary>
    /// Gets the first non-AppStore module or the "MyFirstModule" if it exists
    /// </summary>
    public static IModule? GetMyFirstModule(IModel? model)
    {
        if (model == null)
            return null;

        var modules = model.Root.GetModules();
        return modules.FirstOrDefault(module => module?.Name == "MyFirstModule", null) ??
               modules.First(module => module.FromAppStore == false);
    }

    /// <summary>
    /// Gets a module by name (case-insensitive)
    /// </summary>
    public static IModule? GetModuleByName(IModel? model, string moduleName)
    {
        if (model == null || string.IsNullOrWhiteSpace(moduleName))
            return null;

        return model.Root.GetModules()
            .FirstOrDefault(m => m?.Name != null && m.Name.Equals(moduleName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets all non-AppStore (user-created) modules
    /// </summary>
    public static IEnumerable<IModule> GetAllNonAppStoreModules(IModel? model)
    {
        if (model == null)
            return Enumerable.Empty<IModule>();

        return model.Root.GetModules().Where(m => m != null && !m.FromAppStore);
    }

    /// <summary>
    /// Resolves a module: by name if provided, otherwise falls back to GetMyFirstModule
    /// </summary>
    public static IModule? ResolveModule(IModel? model, string? moduleName)
    {
        if (model == null)
            return null;

        if (!string.IsNullOrWhiteSpace(moduleName))
            return GetModuleByName(model, moduleName);

        return GetMyFirstModule(model);
    }

    /// <summary>
    /// Finds an entity by name, optionally scoped to a module.
    /// When moduleName is null, searches all non-AppStore modules.
    /// </summary>
    public static (IEntity? entity, IModule? module) FindEntityAcrossModules(
        IModel? model, string entityName, string? moduleName = null)
    {
        if (model == null || string.IsNullOrWhiteSpace(entityName))
            return (null, null);

        // Handle qualified names like "ModuleName.EntityName"
        if (entityName.Contains('.') && string.IsNullOrWhiteSpace(moduleName))
        {
            var parts = entityName.Split('.', 2);
            moduleName = parts[0];
            entityName = parts[1];
        }

        if (!string.IsNullOrWhiteSpace(moduleName))
        {
            var module = GetModuleByName(model, moduleName);
            if (module?.DomainModel == null) return (null, null);
            var entity = module.DomainModel.GetEntities()
                .FirstOrDefault(e => e.Name.Equals(entityName, StringComparison.OrdinalIgnoreCase));
            return (entity, entity != null ? module : null);
        }

        foreach (var module in GetAllNonAppStoreModules(model))
        {
            if (module.DomainModel == null) continue;
            var entity = module.DomainModel.GetEntities()
                .FirstOrDefault(e => e.Name.Equals(entityName, StringComparison.OrdinalIgnoreCase));
            if (entity != null)
                return (entity, module);
        }
        return (null, null);
    }

    /// <summary>
    /// Gets the domain model for a given module safely
    /// </summary>
    public static IDomainModel? GetDomainModel(IModule? module)
    {
        return module?.DomainModel;
    }

    /// <summary>
    /// Safely gets all entities from a domain model
    /// </summary>
    public static IEnumerable<IEntity> GetEntities(IDomainModel? domainModel)
    {
        return domainModel?.GetEntities() ?? Enumerable.Empty<IEntity>();
    }

    /// <summary>
    /// Validates if a model and its components are available
    /// </summary>
    public static (bool isValid, string errorMessage) ValidateModel(IModel? model, string? moduleName = null)
    {
        if (model == null)
            return (false, "No current application available.");

        var module = ResolveModule(model, moduleName);
        if (module == null)
        {
            var msg = string.IsNullOrWhiteSpace(moduleName)
                ? "No module found in the application."
                : $"Module '{moduleName}' not found in the application.";
            return (false, msg);
        }

        var domainModel = GetDomainModel(module);
        if (domainModel == null)
            return (false, "No domain model found in the module.");

        return (true, string.Empty);
    }

    /// <summary>
    /// Converts a string representation to a DataType
    /// </summary>
    public static DataType DataTypeFromString(string typeName)
    {
        // Handle null, empty, or whitespace-only strings as Void
        if (string.IsNullOrWhiteSpace(typeName))
            return DataType.Void;
            
        return typeName.ToLower() switch
        {
            "string" => DataType.String,
            "integer" => DataType.Integer,
            "boolean" => DataType.Boolean,
            "decimal" => DataType.Decimal,
            "datetime" => DataType.DateTime,
            "long" => DataType.Integer, // Mendix uses Integer for Long values
            "void" => DataType.Void,
            _ => DataType.String // Default to string for unknown types
        };
    }
}
