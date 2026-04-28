using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace BlocksTemplate.Api;

/// <summary>Prepends a segment (e.g. <c>api</c>) to every controller’s attribute route template.</summary>
internal sealed class GlobalApiRoutePrefixConvention(string routeTemplate) : IApplicationModelConvention
{
    private readonly AttributeRouteModel _prefix = new(new RouteAttribute(routeTemplate));

    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            foreach (var selector in controller.Selectors)
            {
                if (selector.AttributeRouteModel != null)
                {
                    selector.AttributeRouteModel = AttributeRouteModel.CombineAttributeRouteModel(
                        _prefix,
                        selector.AttributeRouteModel);
                }
            }
        }
    }
}
