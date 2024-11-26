using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.NET.HostModel.AppHost;


namespace dotnet9Reflection
{
    internal class Program
    {

        static void Main(string[] args)
        {
            string referencePath = @"C:\Program Files\dotnet\shared\Microsoft.NETCore.App\9.0.0";
            Directory.CreateDirectory("output");
            string basePath = Directory.GetCurrentDirectory();
            basePath = Path.Combine(basePath, "output");

            CopyDirectory(referencePath, basePath, true);
            CopyDirectory("C:\\Program Files\\dotnet\\host\\fxr\\9.0.0", basePath, true);

            referencePath = basePath;
            PathAssemblyResolver resolver = new(Directory.GetFiles(referencePath, "*.dll"));
            using MetadataLoadContext context = new(resolver);
            Assembly coreAssembly = context.CoreAssembly!;
            Type voidType = coreAssembly.GetType(typeof(void).FullName!)!;
            Type objectType = coreAssembly.GetType(typeof(object).FullName!)!;
            Type stringType = coreAssembly.GetType(typeof(string).FullName!)!;
            Type stringArrayType = coreAssembly.GetType(typeof(string[]).FullName!)!;
            Type consoleType = coreAssembly.GetType(typeof(Console).FullName!)!;

            PersistedAssemblyBuilder assemblyBuilder = new(new AssemblyName("HelloWorldTest"), coreAssembly);

            TypeBuilder typeBuilder = assemblyBuilder.DefineDynamicModule("HelloWorldTest").DefineType("HelloWorldTest", TypeAttributes.Public | TypeAttributes.Class, objectType);

            MethodBuilder methodBuilder = typeBuilder.DefineMethod("Main", MethodAttributes.Public | MethodAttributes.Static, voidType, [stringArrayType]);
            ILGenerator ilGenerator = methodBuilder.GetILGenerator();
            ilGenerator.Emit(OpCodes.Ldstr, "Hello, World!");
            ilGenerator.Emit(OpCodes.Call, consoleType.GetMethod("WriteLine", [stringType])!);
            ilGenerator.Emit(OpCodes.Ret);

            typeBuilder.CreateType();

            MetadataBuilder metadataBuilder = assemblyBuilder.GenerateMetadata(out BlobBuilder ilStream, out BlobBuilder fieldData);
            PEHeaderBuilder peHeaderBuilder = new(imageCharacteristics: Characteristics.ExecutableImage);

            ManagedPEBuilder peBuilder = new(
                header: peHeaderBuilder,
                metadataRootBuilder: new MetadataRootBuilder(metadataBuilder),
                ilStream: ilStream,
                mappedFieldData: fieldData,
                entryPoint: MetadataTokens.MethodDefinitionHandle(methodBuilder.MetadataToken));

            BlobBuilder peBlob = new();
            peBuilder.Serialize(peBlob);

            using (FileStream fileStream = new(Path.Combine(basePath, "HelloWorldTest.dll"), FileMode.Create, FileAccess.Write))
            {
                peBlob.WriteContentTo(fileStream);
            }

            
            HostWriter.CreateAppHost(
                @"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Host.win-x64\9.0.0\runtimes\win-x64\native\apphost.exe",
                Path.Combine(basePath, "HelloWorldTest.exe"),
                "HelloWorldTest.dll",false);

//            File.WriteAllText(Path.Combine(basePath,"HelloWorldTest.runtimeconfig.json"), @"{
//  ""runtimeOptions"": {
//    ""tfm"": ""net9.0"",
//    ""framework"": {
//      ""name"": ""Microsoft.NETCore.App"",
//      ""version"": ""9.0.0""
//    }
//  }
//}");
        }
        static void CopyDirectory(string sourceDir, string destinationDir, bool recursive)
        {
            // Get information about the source directory
            var dir = new DirectoryInfo(sourceDir);

            // Check if the source directory exists
            if (!dir.Exists)
                throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

            // Cache directories before we start copying
            DirectoryInfo[] dirs = dir.GetDirectories();

            // Create the destination directory
            Directory.CreateDirectory(destinationDir);

            // Get the files in the source directory and copy to the destination directory
            foreach (FileInfo file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                if(file.Extension == ".dll"|| file.Extension == ".exe")
                {
                    file.CopyTo(targetFilePath,true);

                }
            }

            // If recursive and copying subdirectories, recursively call this method
            if (recursive)
            {
                foreach (DirectoryInfo subDir in dirs)
                {
                    string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                    CopyDirectory(subDir.FullName, newDestinationDir, true);
                }
            }
        }
    }
}
