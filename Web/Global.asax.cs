﻿using System.Configuration;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using Autofac;
using Autofac.Integration.Mvc;
using BookSleeve;
using Compilify.Web.Services;
using Microsoft.Web.Optimization;
using Raven.Abstractions.Data;
using Raven.Client;
using Raven.Client.Document;

namespace Compilify.Web
{
    public class Application : HttpApplication
    {
        protected void Application_Start()
        {
            ViewEngines.Engines.Clear();
            ViewEngines.Engines.Add(new RazorViewEngine());

            MvcHandler.DisableMvcResponseHeader = true;

            ConfigureIoC();
            RegisterGlobalFilters(GlobalFilters.Filters);
            RegisterBundles(BundleTable.Bundles);
            RegisterRoutes(RouteTable.Routes);
        }

        private static void RegisterGlobalFilters(GlobalFilterCollection filters)
        {
            filters.Add(new HandleErrorAttribute());
        }

        private static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            routes.MapRoute(
                name: "Root",
                url: "",
                defaults: new { controller = "Home", action = "Index" },
                constraints: new { httpMethod = new HttpMethodConstraint("GET") }
            );

            routes.MapRoute(
                name: "validate",
                url: "validate",
                defaults: new { controller = "Home", action = "Validate" },
                constraints: new { httpMethod = new HttpMethodConstraint("POST") }
            );

            routes.MapRoute(
                name: "Save",
                url: "{slug}",
                defaults: new { controller = "Home", action = "Save", slug = UrlParameter.Optional },
                constraints: new { httpMethod = new HttpMethodConstraint("POST") }
            );

            routes.MapRoute(
                name: "Show",
                url: "{slug}/{version}",
                defaults: new { controller = "Home", action = "Show", version = UrlParameter.Optional },
                constraints: new { httpMethod = new HttpMethodConstraint("GET") }
            );

            //routes.MapRoute(
            //    name: "Default",
            //    url: "{controller}/{action}",
            //    defaults: new { controller = "Home", action = "Index" }
            //);
        }

        private static void RegisterBundles(BundleCollection bundles)
        {
            var css = new Bundle("~/css", typeof(CssMinify));
            css.AddDirectory("~/assets/css", "*.css", false);
            bundles.Add(css);

            var js = new Bundle("~/js", typeof(JsMinify));
            js.AddDirectory("~/assets/js", "*.js", false);
            bundles.Add(js);
        }

        private static void ConfigureIoC()
        {
            var assembly = typeof(Application).Assembly;
            var builder = new ContainerBuilder();

            builder.Register(x =>
                             {
                                 var conn = new RedisConnection(ConfigurationManager.AppSettings["REDISTOGO_URL"]);
                                 conn.Wait(conn.Open());
                                 return conn;
                             })
                   .InstancePerHttpRequest()
                   .AsSelf();

            builder.Register(x =>
                             {
                                 var parser = ConnectionStringParser<RavenConnectionStringOptions>.FromConnectionStringName("RavenDB");
                                 parser.Parse();

                                 var store = new DocumentStore
                                             {
                                                 ApiKey = parser.ConnectionStringOptions.ApiKey,
                                                 Url = parser.ConnectionStringOptions.Url
                                             };

                                 store.Initialize();

                                 return store;
                             })
                   .SingleInstance()
                   .As<IDocumentStore>();

            builder.RegisterType<DocumentSession>()
                   .As<IDocumentSession>()
                   .InstancePerHttpRequest();

            builder.Register(x => new SequenceProvider(x.Resolve<RedisConnection>()))
                   .AsImplementedInterfaces()
                   .InstancePerHttpRequest();

            builder.RegisterType<PageContentRepository>()
                   .AsImplementedInterfaces()
                   .InstancePerHttpRequest();

            builder.RegisterControllers(assembly);
            builder.RegisterModelBinders(assembly);
            builder.RegisterModelBinderProvider();
            builder.RegisterModule(new AutofacWebTypesModule());
            builder.RegisterFilterProvider();
            var container = builder.Build();
            DependencyResolver.SetResolver(new AutofacDependencyResolver(container));
        }
    }
}
