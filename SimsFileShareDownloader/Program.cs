using CsvHelper;
using CsvHelper.Configuration.Attributes;
using SharpCompress.Readers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SimsFileShareDownloader
{
    class Program
    {
        public static object SyncLock = new object();
        public static string BASEPATH = @"D:\SimsFileShareDump";
        //public static string BASEPATH = @"C:\Users\jeremy\source\repos\SimsFileShareDownloader\SimsFileShareDownloader\bin\Debug\SimsFileShareDump";
        public static bool IsWantingCancellation = false;

        static void Main(string[] args)
        {
            Console.CancelKeyPress += Console_CancelKeyPress;
            FileListing.Load();
            //Files = GetFileListing();
            UnpackAllFiles();
            //WriteFileDownloadStatus();
            //GenerateFileHashs();
            //UnpackAllFiles();
            //GrabAllFileListings();
            //DownloadFiles();
            //PutFileListing();
            FileListing.Save();
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            if (!IsWantingCancellation)
            {
                IsWantingCancellation = true;
                Console.WriteLine();
                Console.WriteLine("Cancelling.... Please wait!!!");
                Console.WriteLine();
            }
        }

        private static void UnpackAllFiles()
        {
            Console.WriteLine("Unpacking All Files");

            var files = FileListing.Items.Where(o => o.OnDisk && !o.Extracted && (o.FileName.EndsWith(".7z", StringComparison.CurrentCultureIgnoreCase) || o.FileName.EndsWith(".zip", StringComparison.CurrentCultureIgnoreCase) || o.FileName.EndsWith(".rar", StringComparison.CurrentCultureIgnoreCase)))
                        .OrderBy(o => o.FolderId).ThenBy(o => o.FileName);

            foreach (var file in files)
            {
                try
                {
                    Console.WriteLine($"{file.FolderId}/{file.FileName}");
                    var foundPackage = false;

                    using (Stream stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1024 * 1024))
                    using (var reader = ReaderFactory.Open(stream))
                    {
                        while (reader.MoveToNextEntry())
                        {
                            if (!reader.Entry.IsDirectory && reader.Entry.Key.ToLower().EndsWith(".package", StringComparison.CurrentCultureIgnoreCase))
                            {
                                if (!File.Exists(Path.Combine(BASEPATH, "FileContents", file.FolderId.ToString(), Path.GetFileName(reader.Entry.Key))))
                                {
                                    Console.WriteLine($"{file.FolderId}/{file.FileName} -> {Path.GetFileName(reader.Entry.Key)} = EXTRACT");
                                    using (var output = File.Create(Path.Combine(BASEPATH, "FileContents", file.FolderId.ToString(), Path.GetFileName(reader.Entry.Key))))
                                    using (var entryStream = reader.OpenEntryStream())
                                    {
                                        entryStream.CopyTo(output, 1024 * 1024);
                                        foundPackage = true;
                                    }
                                }
                                else
                                {
                                    Console.WriteLine($"{file.FolderId}/{file.FileName} -> {Path.GetFileName(reader.Entry.Key)} = EXISTS");
                                    foundPackage = true;
                                }

                            }
                        }
                    }

                    if (foundPackage)
                    {
                        file.Extracted = true;
                        if (File.Exists(file.FullPath))
                            File.Delete(file.FullPath);
                    }

                    Console.WriteLine(" DONE");

                    if (IsWantingCancellation)
                        return;
                }
                catch
                {
                    Console.WriteLine(" FAIL");
                }
            }
        }

        private static List<FileListing> GetFileListing()
        {
            var list = new List<FileListing>();
            using (var textreader = File.OpenText(Path.Combine(BASEPATH, "FileListing.DAT")))
            using (var csvreader = new CsvReader(textreader))
                list.Add(csvreader.GetRecord<FileListing>());
            return list;
        }

        private static void PutFileListing()
        {
            Console.Write("Writing file listing database...");

            using (var textwriter = File.CreateText(Path.Combine(BASEPATH, "FileContents.DAT")))
            using (var output = new CsvWriter(textwriter))
            {
                output.WriteHeader<FileListing>();
                output.WriteRecords(FileListing.Items.OrderBy(o => o.FolderId).ThenBy(p => p.FileName));
            }

            Console.Write("DONE");
        }

        private static void GrabAllFileListings()
        {
            var downloaded = 0;
            var blocksize = 1000;

            for (var c = 75000; c < 100000; c += blocksize)
            {
                Console.WriteLine($"Starting on block {c}");
                Parallel.For(c, c + blocksize, new ParallelOptions { MaxDegreeOfParallelism = 8 }, x =>
                {
                    try
                    {
                        var webClient = new WebClient();

                        webClient.DownloadFile($"https://simfileshare.net/folder/{x}/", $"{x}.filelist");
                        downloaded++;
                        if (downloaded > 0 && downloaded % 25 == 0)
                            Console.WriteLine($"Downloaded {downloaded} item lists");
                        Task.Delay(15).Wait();
                    }
                    catch (Exception ex)
                    {
                    }
                });
            }
        }



        private static void DownloadFiles()
        {
            Console.WriteLine("Files processed... Downloading");

            if (!Directory.Exists(Path.Combine(BASEPATH, "FileContents")))
                Directory.CreateDirectory(Path.Combine(BASEPATH, "FileContents"));

            var success = 0;
            var failure = 0;

            Parallel.ForEach(FileListing.Items, new ParallelOptions { MaxDegreeOfParallelism = 16 }, entry =>
            {
                try
                {
                    if (!Directory.Exists(Path.Combine(BASEPATH, "FileContents", entry.FolderId.ToString())))
                        Directory.CreateDirectory(Path.Combine(BASEPATH, "FileContents", entry.FolderId.ToString()));

                    var fullpath = Path.Combine(BASEPATH, "FileContents", entry.FolderId.ToString(), entry.FileName);

                    Console.WriteLine($"{success} / {failure} / {fullpath}");

                    if (!File.Exists(fullpath))
                    {

                        try
                        {
                            if (!File.Exists(fullpath))
                            {
                                var wc = new WebClient();
                                wc.DownloadFile($"https://cdn.simfileshare.net/download/{entry.FileId}/?dl", Path.Combine(BASEPATH, "FileContents", entry.FolderId.ToString(), entry.FileName + ".DOWNLOAD"));
                                File.Move(Path.Combine(BASEPATH, "FileContents", entry.FolderId.ToString(), entry.FileName + ".DOWNLOAD"), Path.Combine(BASEPATH, "FileContents", entry.FolderId.ToString(), entry.FileName));
                            }
                            success++;
                        }
                        catch
                        {
                            failure++;
                        }
                    }
                }
                catch
                {
                }
            });
        }

        public static IEnumerable<FileListing> WriteDATFromFileListings()
        {
            var t = new List<FileListing>();
            var regex = new Regex("<td><a href=\"\\/download\\/(\\d{1,10})\\/\">(.+?)</a></td>", RegexOptions.Multiline);
            var regex2 = new Regex("<h4>(.+?)<\\/h4>", RegexOptions.Multiline);

            Parallel.ForEach(Directory.EnumerateFiles(Path.Combine(BASEPATH, "FileListings"), "*.filelist"), new ParallelOptions { MaxDegreeOfParallelism = 8 }, file =>
            {
                var position = int.Parse(Path.GetFileNameWithoutExtension(file));
                var matches = regex.Matches(File.ReadAllText(file));

                foreach (Match match in matches)
                {
                    var key = int.Parse(match.Groups[1].Value);
                    if (!string.IsNullOrEmpty(match.Groups[2].Value))
                        t.Add(new FileListing { FolderId = position, FileId = key, FileName = match.Groups[2].Value });
                }
            });

            return t.Where(o => o != null);
        }
    }

    [Serializable]
    public class FileListing
    {
        private string _safeFileName = string.Empty;
        private string _md5Hash = string.Empty;
        public static List<FileListing> Items = null;

        public static void Load()
        {
            if (File.Exists(Path.Combine(Program.BASEPATH, "FileContents.DAT")))
            {
                try
                {
                    using (var input = File.Open(Path.Combine(Program.BASEPATH, "FileContents.DAT"), FileMode.Open))
                        Items = (List<FileListing>)new BinaryFormatter().Deserialize(input);
                }
                catch (Exception)
                {
                    throw new Exception("Could not load database file... restore from backup!");
                }
            }
            else
            {
                Console.WriteLine("NOTICE: Creating new FileContents Database file!");
                FileListing.Items = new List<FileListing>();
                FileListing.Items.AddRange(Program.WriteDATFromFileListings());
                Save();
            }
        }

        public static void Save()
        {
            if (File.Exists(Path.Combine(Program.BASEPATH, "FileContents.DAT")))
            {
                File.Delete(Path.Combine(Program.BASEPATH, "FileContents.BAK"));
                File.Move(Path.Combine(Program.BASEPATH, "FileContents.DAT"), Path.Combine(Program.BASEPATH, "FileContents.BAK"));
            }
            using (var output = File.Create(Path.Combine(Program.BASEPATH, "FileContents.DAT")))
                new BinaryFormatter().Serialize(output, Items);
        }

        public int FolderId { get; set; }

        public int FileId { get; set; }

        public string Url => $"https://cdn.simfileshare.net/download/{FileId}/?dl";

        public string SafeFileName
        {
            get
            {
                if (string.IsNullOrEmpty(_safeFileName))
                {
                    _safeFileName = FileName;
                    foreach (var c in Path.GetInvalidFileNameChars())
                        _safeFileName = _safeFileName.Replace(c, '_');
                }
                return _safeFileName;
            }
        }

        public string FileName { get; set; }

        public string FullPath => Path.Combine(Program.BASEPATH, "FileContents", FolderId.ToString(), SafeFileName);

        public DateTime DateChecked => DateTime.Now;

        public bool OnDisk => File.Exists(Path.Combine(Program.BASEPATH, "FileContents", FolderId.ToString(), SafeFileName));

        //public string Md5Hash => GetMD5SUM();

        private string GetMD5SUM()
        {
            if (OnDisk && string.IsNullOrEmpty(_md5Hash))
                if (File.Exists(FullPath))
                {
                    var md5 = MD5.Create();
                    using (var input = File.OpenRead(FullPath))
                        _md5Hash = ToHex(md5.ComputeHash(input), true);
                }
            return _md5Hash;
        }

        public bool Extracted { get; set; }

        private static string ToHex(byte[] bytes, bool upperCase)
        {
            StringBuilder result = new StringBuilder(bytes.Length * 2);

            for (int i = 0; i < bytes.Length; i++)
                result.Append(bytes[i].ToString(upperCase ? "X2" : "x2"));

            return result.ToString();
        }

        public override string ToString()
        {
            return $"{FolderId} / {FileName}";
        }
    }
}
