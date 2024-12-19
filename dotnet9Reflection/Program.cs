using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.NET.HostModel.AppHost;
using System.IO;
using Microsoft.NET.HostModel.Bundle;
using System.Runtime.Loader;


namespace dotnet9Reflection
{
    internal class Program
    {

        static void Main(string[] args)
        {

            // 出力パスを作成
            DirectoryInfo baseDirectory = Directory.CreateDirectory("output");

            // ライブラリなどがあるディレクトリを取得
            string referencePath = GetVersionDirectory(@"C:\Program Files\dotnet\shared\Microsoft.NETCore.App",9,0)!;
            
            // 保存先パスを取得
            string basePath = baseDirectory.FullName;
            
            // sdkのパスを取得
            string sdkPath = @"C:\Program Files\dotnet\sdk";

            // dllをコピー
            CopyDirectory(referencePath, basePath, true);

            // hostfxrをコピー
            string fxrDirPath = GetVersionDirectory(@"C:\Program Files\dotnet\host\fxr", 9, 0)!;
            CopyDirectory(fxrDirPath, basePath, true);

            // dllなどをコピーしたのでコピー先のdllを参照するように指定する
            referencePath = basePath;

            // dllを読み込み
            PathAssemblyResolver resolver = new(Directory.GetFiles(referencePath, "*.dll"));
            using MetadataLoadContext context = new(resolver);
            Assembly coreAssembly = context.CoreAssembly!;
            
            // typeを取得
            Type voidType = coreAssembly.GetType(typeof(void).FullName!)!;
            Type objectType = coreAssembly.GetType(typeof(object).FullName!)!;
            Type stringType = coreAssembly.GetType(typeof(string).FullName!)!;
            Type stringArrayType = coreAssembly.GetType(typeof(string[]).FullName!)!;
            Type consoleType = coreAssembly.GetType(typeof(Console).FullName!)!;

            // PersistedAssemblyBuilderを生成
            PersistedAssemblyBuilder assemblyBuilder = new(new AssemblyName("HelloWorldTest"), coreAssembly);

            // TypeBuilderの生成
            TypeBuilder typeBuilder = assemblyBuilder.DefineDynamicModule("HelloWorldTest").DefineType("HelloWorldTest", TypeAttributes.Public | TypeAttributes.Class, objectType);

            // メソッドを生成
            MethodBuilder methodBuilder = typeBuilder.DefineMethod("Main", MethodAttributes.Public | MethodAttributes.Static, voidType, [stringArrayType]);
            //メソッド内部を生成
            ILGenerator ilGenerator = methodBuilder.GetILGenerator();
            ilGenerator.Emit(OpCodes.Ldstr, "Hello, World!");
            ilGenerator.Emit(OpCodes.Call, consoleType.GetMethod("WriteLine", [stringType])!);
            ilGenerator.Emit(OpCodes.Ret);

            
            // HelloWorldTestの型を生成
            typeBuilder.CreateType();

            
            // PersistedAssemblyBuilderからMetadataBuilderを生成
            MetadataBuilder metadataBuilder = assemblyBuilder.GenerateMetadata(out BlobBuilder ilStream, out BlobBuilder fieldData);
            // PEファイル(DLLファイル)のヘッダーを生成
            PEHeaderBuilder peHeaderBuilder = new(imageCharacteristics: Characteristics.ExecutableImage);

            ManagedPEBuilder peBuilder = new(
                header: peHeaderBuilder,
                metadataRootBuilder: new MetadataRootBuilder(metadataBuilder),
                ilStream: ilStream,
                mappedFieldData: fieldData,
                entryPoint: MetadataTokens.MethodDefinitionHandle(methodBuilder.MetadataToken));
            
            // BlobBuilerへ変換
            BlobBuilder peBlob = new();
            peBuilder.Serialize(peBlob);
            
            // blobからStreamに書き込み
            using (FileStream fileStream = new(Path.Combine(basePath, "HelloWorldTest.dll"), FileMode.Create, FileAccess.Write))
            {
                peBlob.WriteContentTo(fileStream);
            }


            // HostWriter.CreateAppHostメソッドのMethodInfoを取得
            var CreateAppHost = SearchHostWriterDelegate(sdkPath, 9, 0);

            CreateAppHost.Invoke(null, new object[]{
                @"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Host.win-x64\9.0.0\runtimes\win-x64\native\apphost.exe",
                Path.Combine(basePath, "HelloWorldTest.exe"),
                "HelloWorldTest.dll",false,null,false,false,null });

            //HostWriter.CreateAppHost(
            //    @"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Host.win-x64\9.0.0\runtimes\win-x64\native\apphost.exe",
            //    Path.Combine(basePath, "HelloWorldTest.exe"),
            //    "HelloWorldTest.dll",false);

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
        static string? GetVersionDirectory(string path, int majorVersion ,int minorVersion, int revisionVersion = -1,int buildVersion = -1)
        {

            DirectoryInfo directoryInfo = new DirectoryInfo(path);
            DirectoryInfo[] directories = directoryInfo.GetDirectories();
            foreach(DirectoryInfo directory in directories.OrderByDescending(x => new Version(x.Name)))
            {
                Version version = new Version(directory.Name);
                if(version.Major != majorVersion)
                {
                    continue;
                }

                if(version.Minor != minorVersion)
                {
                    continue ;
                }

                if(version.Revision != revisionVersion && revisionVersion != -1)
                {
                    continue;
                }

                if(version.Build !=buildVersion && buildVersion != -1)
                {
                    continue;
                }

                return directory.FullName;
            }

            return null;
        }

        static MethodInfo SearchHostWriterDelegate(string sdkPath,int majorVersion, int minorVersion, int revisionVersion = -1, int buildVersion = -1)
        {
            string hostModelPath = Path.Combine(GetVersionDirectory(sdkPath,majorVersion, minorVersion, revisionVersion, buildVersion)!, "Microsoft.NET.HostModel.dll");
            MethodInfo methodInfo = Assembly.LoadFile(hostModelPath).GetType("Microsoft.NET.HostModel.AppHost.HostWriter")!.GetMethod("CreateAppHost")!;
            return methodInfo;
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
                    Directory.CreateDirectory(newDestinationDir);
                    CopyDirectory(subDir.FullName, newDestinationDir, true);
                }
            }
        }
    }
}
