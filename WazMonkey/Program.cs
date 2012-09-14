using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CommandLine;
using CommandLine.Text;
using System.Xml.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Net;
using System.Globalization;

namespace WazMonkey
{
    class Options : CommandLineOptionsBase
    {
        [Option("p", "publishSettings", Required = false, HelpText = ".publishSettings file - specify either this or a .pfx file")]
        public string PublishSettingsFile { get; set; }

        [Option(null, "pfx", Required = false, HelpText = ".pfx certificate file - specify either this or a .publishSettings file")]
        public string PfxFile { get; set; }

        [Option(null, "subscriptionId", Required = false, HelpText = "subscriptionId to use, defaults to first subscription found in the .publishSettings file")]
        public string SubscriptionId { get; set; }

        [Option("n", "serviceName", Required = true, HelpText = "Name of the cloud service")]
        public string ServiceName { get; set; }

        [Option("s", "slot", Required = true, HelpText = @"The slot (""production"" or ""staging"")")]
        public string Slot { get; set; }

        [HelpOption(HelpText = "Display this help screen.")]
        public string GetUsage()
        {
            var help = new HelpText
            {
                Heading = new HeadingInfo("WazMonkey", "v0.1"),
                Copyright = new CopyrightInfo("Steve Marx", 2012),
                AdditionalNewLineAfterOption = true,
                AddDashesToOption = true
            };
            help.AddPreOptionsLine("Usage: WazMonkey -p foo.publishSettings -n myservice -s production");
            help.AddOptions(this);
            return help;
        }
 }

    class Program
    {
        static void Main(string[] args)
        {
            var options = new Options();
            if (CommandLineParser.Default.ParseArguments(args, options))
            {
                if (options.PublishSettingsFile == null && options.PfxFile == null)
                {
                    Console.WriteLine("Required: one of --publishSettings or --pfx");
                    Console.WriteLine(options.GetUsage());
                    Environment.Exit(1);
                }
                if (options.PublishSettingsFile != null && options.PfxFile != null)
                {
                    Console.WriteLine("Incompatible arguments: --publishSettings and --pfx");
                    Console.WriteLine(options.GetUsage());
                    Environment.Exit(1);
                }
                if (options.PublishSettingsFile == null && options.SubscriptionId == null)
                {
                    Console.WriteLine("--subscriptionId must be provided if no .publishSettings file is being used");
                    Console.WriteLine(options.GetUsage());
                    Environment.Exit(1);
                }
                if (options.Slot != "production" && options.Slot != "staging")
                {
                    Console.WriteLine(@"Slot must be one of ""production"" or ""staging""");
                    Console.WriteLine(options.GetUsage());
                    Environment.Exit(1);
                }
            }
            else
            {
                Environment.Exit(1);
            }

            X509Certificate2 cert;
            string subscriptionId = options.SubscriptionId;

            if (options.PublishSettingsFile != null)
            {
                var profile = XDocument.Load(options.PublishSettingsFile);
                if (subscriptionId == null)
                {
                    subscriptionId = profile.Descendants("Subscription").First().Attribute("Id").Value;
                }
                cert = new X509Certificate2(Convert.FromBase64String(
                    profile.Descendants("PublishProfile").Single()
                    .Attribute("ManagementCertificate").Value));
            }
            else
            {
                cert = new X509Certificate2(options.PfxFile);
            }

            var baseAddress = string.Format("https://management.core.windows.net/{0}/services/hostedservices/{1}/deploymentslots/{2}", subscriptionId, options.ServiceName, options.Slot);

            // request details on specified deployment
            var req = (HttpWebRequest)WebRequest.Create(baseAddress);
            req.Headers["x-ms-version"] = "2012-03-01";
            req.ClientCertificates.Add(cert);

            // find all instances
            XNamespace xmlns = "http://schemas.microsoft.com/windowsazure";
            try
            {
                var responseStream = req.GetResponse().GetResponseStream();
            }
            catch (WebException e)
            {
                if (((HttpWebResponse)e.Response).StatusCode == HttpStatusCode.NotFound)
                {
                    var titleCaseSlot = new CultureInfo("en-us", false).TextInfo.ToTitleCase(options.Slot);
                    Console.WriteLine("Error: {0} deployment for service {1} not found, or the credentials were invalid.", titleCaseSlot, options.ServiceName);
                    Environment.Exit(1);
                }
                else
                {
                    throw;
                }
            }
            var instances = XDocument.Load(req.GetResponse().GetResponseStream()).Descendants(xmlns + "InstanceName").Select(n => n.Value).ToArray();

            // choose one at random
            var instance = instances[new Random().Next(instances.Length)];
            Console.WriteLine("Rebooting {0}.", instance);

            // reboot it
            req = (HttpWebRequest)WebRequest.Create(string.Format("{0}/roleinstances/{1}?comp=reboot", baseAddress, instance));
            req.Method = "POST";
            req.ContentLength = 0;
            req.Headers["x-ms-version"] = "2012-03-01";
            req.ClientCertificates.Add(cert);

            // make sure the response was "accepted"
            var response = (HttpWebResponse)req.GetResponse();
            if (response.StatusCode != HttpStatusCode.Accepted)
            {
                Console.WriteLine("Got unexpected status code: {0}", response.StatusCode);
            }
        }
    }
}
