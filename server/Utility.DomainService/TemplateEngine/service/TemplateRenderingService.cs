using Blocks.Genesis;
using DotLiquid;
using DotLiquid.NamingConventions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Utility.DomainService.TemplateEngine.service
{
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    public class TemplateRenderingService
    {
        private readonly ILogger<TemplateRenderingService> _logger;

        public TemplateRenderingService(ILogger<TemplateRenderingService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Renders a template with JSON data using DotLiquid
        /// </summary>
        public string RenderTemplateWithJson(string templateContent, string jsonString)
        {
            _logger.LogInformation("RenderTemplateWithJson: Starting template rendering");

            // Configure DotLiquid
            Template.NamingConvention = new CSharpNamingConvention();

            // Parse template
            _logger.LogInformation("RenderTemplateWithJson: Parsing template content");
            var parsedTemplate = Template.Parse(templateContent);

            // Deserialize JSON to dictionary
            _logger.LogInformation("RenderTemplateWithJson: Deserializing JSON data");
            var dataDictionary = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString) 
                ?? new Dictionary<string, object>();

            // Add security token from context
            var context = BlocksContext.GetContext();
            if (context != null && !dataDictionary.ContainsKey("securityToken"))
            {
                dataDictionary.Add("securityToken", "");
                // Note: In production, you'd get the actual OAuth token from the context
                // For now, we'll leave it as empty string
            }

            // Convert to Liquid Hash
            var hash = Hash.FromDictionary(dataDictionary);

            // Render
            _logger.LogInformation("RenderTemplateWithJson: Rendering template");
            var result = parsedTemplate.Render(hash);

            _logger.LogInformation("RenderTemplateWithJson: Template rendered successfully, length={Length}", result.Length);
            return result;
        }

        /// <summary>
        /// Renders a template with entity data and metadata
        /// </summary>
        public string RenderTemplateWithEntityData(
            string templateContent,
            Dictionary<string, object> entities,
            Dictionary<string, object> metadata)
        {
            _logger.LogInformation("RenderTemplateWithEntityData: Starting template rendering");

            // Configure DotLiquid
            Template.NamingConvention = new CSharpNamingConvention();

            // Parse template
            _logger.LogInformation("RenderTemplateWithEntityData: Parsing template");
            var parsedTemplate = Template.Parse(templateContent);

            // Combine entities and metadata
            var dataDictionary = new Dictionary<string, object>();
            
            _logger.LogInformation("RenderTemplateWithEntityData: Adding {EntityCount} entities", entities.Count);
            foreach (var entity in entities)
            {
                dataDictionary[entity.Key] = entity.Value;
            }

            _logger.LogInformation("RenderTemplateWithEntityData: Adding {MetadataCount} metadata items", metadata.Count);
            foreach (var meta in metadata)
            {
                dataDictionary[meta.Key] = meta.Value;
            }

            // Add security token
            if (!dataDictionary.ContainsKey("securityToken"))
            {
                dataDictionary.Add("securityToken", "");
            }

            _logger.LogInformation("RenderTemplateWithEntityData: Converting to Liquid Hash");
            // Serialize and deserialize to ensure proper type handling
            var jsonString = JsonConvert.SerializeObject(dataDictionary);
            var finalDictionary = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonString) 
                ?? new Dictionary<string, object>();

            // Convert to Liquid Hash
            var hash = Hash.FromDictionary(finalDictionary);

            // Render
            _logger.LogInformation("RenderTemplateWithEntityData: Rendering template");
            var result = parsedTemplate.Render(hash);

            _logger.LogInformation("RenderTemplateWithEntityData: Template rendered successfully, length={Length}", result.Length);
            return result;
        }
    }
}


