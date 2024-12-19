using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

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
            PersistedAssemblyBuilder assemblyBuilder = new(new AssemblyName("EmitTest"), coreAssembly);

            // TypeBuilderの生成
            TypeBuilder typeBuilder = assemblyBuilder.DefineDynamicModule("EmitTest").DefineType("Program", TypeAttributes.Public | TypeAttributes.Class, objectType);

            // メソッドを生成
            MethodBuilder methodBuilder = typeBuilder.DefineMethod("Main", MethodAttributes.Public | MethodAttributes.Static, voidType, [stringArrayType]);
            //メソッド内部を生成
            ILGenerator ilGenerator = methodBuilder.GetILGenerator();
            ilGenerator.Emit(OpCodes.Ldstr, "Hello, World!");
            ilGenerator.Emit(OpCodes.Call, consoleType.GetMethod("WriteLine", [stringType])!);
            ilGenerator.Emit(OpCodes.Ret);

            
            // EmitTestの型を生成
            typeBuilder.CreateType();

            
            // PersistedAssemblyBuilderからMetadataBuilderを生成
            MetadataBuilder metadataBuilder = assemblyBuilder.GenerateMetadata(out BlobBuilder ilStream, out BlobBuilder fieldData);
            // PEファイル(DLLファイル)のヘッダーを生成
            PEHeaderBuilder peHeaderBuilder = new(imageCharacteristics: Characteristics.ExecutableImage);
            // CLR上で動くPEファイルの作成
            ManagedPEBuilder peBuilder = new(
                header: peHeaderBuilder,
                metadataRootBuilder: new MetadataRootBuilder(metadataBuilder),
                ilStream: ilStream,
                mappedFieldData: fieldData,
                entryPoint: MetadataTokens.MethodDefinitionHandle(methodBuilder.MetadataToken));
            
            // PEデータからBlobBuilerへ変換
            BlobBuilder peBlob = new();
            peBuilder.Serialize(peBlob);
            
            // blobからStreamに書き込み(dllの書き込み)
            using (FileStream fileStream = new(Path.Combine(basePath, "EmitTest.dll"), FileMode.Create, FileAccess.Write))
            {
                peBlob.WriteContentTo(fileStream);
            }


            // HostWriter.CreateAppHostメソッドのMethodInfoを取得
            var CreateAppHost = SearchHostWriterMethodInfo(sdkPath, 9, 0);

            // ベースとなるapphostのパスを取得
            string packPath = @"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Host.win-x64";
            string apphostPath = Path.Combine(GetVersionDirectory(packPath, 9, 0)!, "runtimes", "win-x64", "native", "apphost.exe");

            // exeファイルの書き込み
            CreateAppHost.Invoke(null, new object[]{
                apphostPath,
                Path.Combine(basePath, "EmitTest.exe"),
                "EmitTest.dll",false,null,false,false,null });

            // runtimeconfig.jsonファイルを生成
            File.WriteAllText(Path.Combine(basePath, "EmitTest.runtimeconfig.json"), @"{
              ""runtimeOptions"": {
                ""tfm"": ""net9.0"",
                ""includedFrameworks"": [
                  {
                    ""name"": ""Microsoft.NETCore.App"",
                    ""version"": ""9.0.0""
                  }
                ]
              }
            }");
        }

        /// <summary>
        /// サブフォルダの名前をVersionとみなし条件を満たす最新のversionの名前のフォルダを取得する
        /// </summary>
        /// <param name="path"></param>
        /// <param name="majorVersion"></param>
        /// <param name="minorVersion"></param>
        /// <param name="revisionVersion"></param>
        /// <param name="buildVersion"></param>
        /// <returns></returns>
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

        /// <summary>
        /// Microsoft.NET.HostModel.dll内のHostWriterクラスのCreateAppHostのメソッドをMethodInfoとして取得
        /// </summary>
        /// <param name="sdkPath">sdkのあるパス</param>
        /// <param name="majorVersion">majorVersion</param>
        /// <param name="minorVersion">minorVersion</param>
        /// <param name="revisionVersion">revisionVersion</param>
        /// <param name="buildVersion">buildVersion</param>
        /// <returns></returns>
        static MethodInfo SearchHostWriterMethodInfo(string sdkPath,int majorVersion, int minorVersion, int revisionVersion = -1, int buildVersion = -1)
        {
            string hostModelPath = Path.Combine(GetVersionDirectory(sdkPath,majorVersion, minorVersion, revisionVersion, buildVersion)!, "Microsoft.NET.HostModel.dll");
            MethodInfo methodInfo = Assembly.LoadFile(hostModelPath).GetType("Microsoft.NET.HostModel.AppHost.HostWriter")!.GetMethod("CreateAppHost")!;
            return methodInfo;
        }

        /// <summary>
        /// dllをコピーするメソッド
        /// </summary>
        /// <param name="sourceDir">コピー元directory</param>
        /// <param name="destinationDir">コピー元先</param>
        /// <param name="recursive">サブフォルダを含めるか</param>
        /// <exception cref="DirectoryNotFoundException"></exception>
        static void CopyDirectory(string sourceDir, string destinationDir, bool recursive)
        {
            var dir = new DirectoryInfo(sourceDir);

            if (!dir.Exists)
                throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

            DirectoryInfo[] dirs = dir.GetDirectories();

            Directory.CreateDirectory(destinationDir);

            foreach (FileInfo file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                if(file.Extension == ".dll"|| file.Extension == ".exe")
                {
                    file.CopyTo(targetFilePath,true);

                }
            }

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
