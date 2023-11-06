using System.Collections.Generic;
using Amazon.CDK;
using Amazon.CDK.AWS.CloudWatch;
using Cdk.Builder;
using Cdk.Config;

namespace Cdk
{
    sealed class Program
    {
        public static void Main(string[] args)
        {
            var app = new App();

            // TODO:
            // Define which environment we are in & build the cooresponding config

            var globalConfig = new ExampleLocalConfig();
            var globalItems = new AppStackBuilder(app, globalConfig)
                //.WithOpenSearch()
                .WithVpc(out var vpc)
                .WithContainerCluster(out var cluster, vpc);

            var notificationConfig = new NotificationLocalConfig();
            var notificationService = new ServiceStackBuilder(app, notificationConfig)
                .WithContainerRegistry()
//                .WithDatabaseCluster()
                .WithService(cluster);
                //.WithClusterInstance()
                //.WithSnsTopic("sendEmailRequest", out var topic)
                //.WithSqsQueue("sendEmailQueue", out var queue)
                //.WithSubscription(topic, queue)

            app.Synth();
        }
    }
}
