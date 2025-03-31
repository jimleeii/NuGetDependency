using System.Reflection;

namespace NuGetDependency;

public class Program
{
    public static async Task Main(string[] args)
    {
        string localPackages = @"C:\Users\jimle\source\repos\plugins"; // Replace with your local folder

        using var loader = new NuGetLoader(localSource: localPackages);

        // Install package and dependencies
        await loader.InstallPackageAsync("JointLengthSequencing", "1.0.0");

        // Load the main assembly
        // var assembly = Assembly.Load("System.Text.Json");
        // Console.WriteLine($"Loaded assembly: {assembly.FullName}");
        var assembly = Assembly.Load("JointLengthSequencing");
        Console.WriteLine($"Loaded assembly: {assembly.FullName}");

        //// Find the JsonSerializer type
        //var jsonConvertType = assembly.GetType("System.Text.Json.JsonSerializer");

        //if (jsonConvertType != null)
        //{
        //    // Get the Serialize method (static)
        //    var serializeObjectMethods = jsonConvertType.GetMethods().Where(m => m.Name == "Serialize").Where(m => m.ReturnType == typeof(string) && m.GetParameters().Length == 2);
        //    var serializeObjectMethod = serializeObjectMethods.FirstOrDefault(m => m.GetParameters()[0].ParameterType == typeof(object));

        //    if (serializeObjectMethod != null && serializeObjectMethod.IsStatic)
        //    {
        //        // Example object to serialize
        //        var objectToSerialize = new { Name = "Test", Value = 123 };

        //        // Invoke the static method using null for the instance
        //        var serializedJson = serializeObjectMethod.Invoke(null, [objectToSerialize, null]);

        //        Console.WriteLine($"Serialized JSON: {serializedJson}");
        //    }
        //    else
        //    {
        //        Console.WriteLine("Serialize method not found or not static.");
        //    }
        //}
        //else
        //{
        //    Console.WriteLine("JsonSerializer type not found.");
        //}

        // Dispose of the loader to clean up resources
        loader.Dispose();
    }
}