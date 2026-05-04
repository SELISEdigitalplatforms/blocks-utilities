using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using System.Runtime.Serialization;

namespace XUnitTest.TestHelpers
{
    internal static class ControllerTestHelper
    {
        internal static ChangeControllerContext CreateChangeControllerContext()
        {
            try
            {
                var services = new ServiceCollection();
                services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
                ApplicationConfigurations.ConfigureApi(services);

                var provider = services.BuildServiceProvider();
                var context = provider.GetService<ChangeControllerContext>();

                if (context != null)
                {
                    return context;
                }
            }
            catch
            {
            }

            try
            {
                return new Moq.Mock<ChangeControllerContext>(CreateConstructorArgumentsForMock()).Object;
            }
            catch
            {
                var constructors = typeof(ChangeControllerContext)
                    .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .OrderBy(c => c.GetParameters().Length)
                    .ToArray();

                foreach (var constructor in constructors)
                {
                    try
                    {
                        var args = constructor
                            .GetParameters()
                            .Select(p => CreateArgument(p.ParameterType, p.HasDefaultValue ? p.DefaultValue : null))
                            .ToArray();

                        return (ChangeControllerContext)constructor.Invoke(args);
                    }
                    catch
                    {
                    }
                }

#pragma warning disable SYSLIB0050
                return (ChangeControllerContext)FormatterServices.GetUninitializedObject(typeof(ChangeControllerContext));
#pragma warning restore SYSLIB0050
            }
        }

        private static object?[] CreateConstructorArgumentsForMock()
        {
            var constructor = typeof(ChangeControllerContext)
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .OrderBy(c => c.GetParameters().Length)
                .FirstOrDefault();

            if (constructor == null)
            {
                return Array.Empty<object>();
            }

            return constructor
                .GetParameters()
                .Select(p => CreateArgument(p.ParameterType, p.HasDefaultValue ? p.DefaultValue : null))
                .ToArray();
        }

        private static object? CreateArgument(Type parameterType, object? defaultValue)
        {
            if (defaultValue != null)
            {
                return defaultValue;
            }

            if (parameterType == typeof(IHttpContextAccessor))
            {
                return new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
            }

            if (parameterType == typeof(IConfiguration))
            {
                return new ConfigurationBuilder().Build();
            }

            if (parameterType.IsGenericType && parameterType.GetGenericTypeDefinition() == typeof(ILogger<>))
            {
                var loggerType = typeof(NullLogger<>).MakeGenericType(parameterType.GetGenericArguments()[0]);
                return loggerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            }

            if (parameterType.IsInterface || parameterType.IsAbstract)
            {
                var mockType = typeof(Moq.Mock<>).MakeGenericType(parameterType);
                return mockType.GetProperty("Object")?.GetValue(Activator.CreateInstance(mockType));
            }

            if (parameterType.IsValueType)
            {
                return Activator.CreateInstance(parameterType);
            }

            return null;
        }
    }
}
