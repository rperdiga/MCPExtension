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
    public static (bool isValid, string errorMessage) ValidateModel(IModel? model)
    {
        if (model == null)
            return (false, "No current application available.");

        var module = GetMyFirstModule(model);
        if (module == null)
            return (false, "No module found in the application.");

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
        return typeName.ToLower() switch
        {
            "string" => DataType.String,
            "integer" => DataType.Integer,
            "boolean" => DataType.Boolean,
            "decimal" => DataType.Decimal,
            "datetime" => DataType.DateTime,
            "long" => DataType.Integer, // Mendix uses Integer for Long values
            _ => DataType.String // Default to string for unknown types
        };
    }
}
