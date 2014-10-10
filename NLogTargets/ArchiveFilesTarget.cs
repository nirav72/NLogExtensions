using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Ionic.Zip;
using Ionic.Zlib;
using NLog;
using NLog.Common;
using NLog.Config;
using NLog.Targets;

namespace NLogExtensions.Targets
{
    [Target("ArchiveFiles")] 
    public class ArchiveFilesTarget:FileTarget
    {
        private readonly Regex _datePatternRegex = new Regex(@"\{.*\}", RegexOptions.CultureInvariant);
        private String _zipDateFormat;
        private String _compressedFilePath;
        /// <summary>
        /// Flag indicate is zip compression already started
        /// </summary>
        private volatile Int32 _compressingStarted;

        /// <summary>
        /// How many files have to be rolled before compressing started
        /// </summary>
        [DefaultValue(1)]
        public Int32 FilesThreshold { get; set; }

        /// <summary>
        /// Get or set compression level for backup archiving(Level0,Level5,BestSpeed,BestCompression and etc)
        /// </summary>
        [DefaultValue(typeof(CompressionLevel), "Level9")]
        public CompressionLevel CompressionLevel { set; get; }

        [RequiredParameter]
        public String ZipFile
        {
            set
            {
				if (String.IsNullOrWhiteSpace(value))
					return;
                String compressedFileName = Path.GetFileName(value);
                //find datetime pattern in passed compressed file name
                Match match = _datePatternRegex.Match(compressedFileName);
                if (!match.Success) 
                    return;
                String pattern = match.Value.Trim('{', '}');
                //try to get current datetime string representation in form of custom datetime pattern(which we find in current value)
                try
                {
                    //validate datetime pattern
                    DateTime.Now.ToString(pattern, CultureInfo.InvariantCulture);
                    //if valid,save it for later use in creating compressed file name
                    _zipDateFormat = pattern;
                }
                catch (FormatException)
                {
                    _zipDateFormat = "yyyy-MM-dd_HH-mm-ss";
                }
                _compressedFilePath = value;
            }
            get
            {
                return _compressedFilePath;
            } 
        }

        protected override void Write(LogEventInfo logEvent)
        {
            base.Write(logEvent);
            TryCompress(logEvent);
        }

        /// <summary>
        /// Method check conditions to start compression process
        /// </summary>
        /// <param name="logEvent">Current logging event</param>
        protected virtual void TryCompress(LogEventInfo logEvent)
        {
            if(Interlocked.CompareExchange(ref _compressingStarted,1,0)!=0)
                return;
            String archiveDirectory;
            String archiveFilePattern;
            try
            {
                archiveDirectory = Path.GetDirectoryName(ArchiveFileName.Render(logEvent));
                archiveFilePattern = Path.GetFileName(ArchiveFileName.Render(logEvent));
            }
            catch (Exception ex)
            {
                InternalLogger.Error("Error while render layout ArchiveFileName={0}{1}{2}",
                                        ArchiveFileName,
                                        Environment.NewLine,
                                        ex.ToString());
                _compressingStarted = 0;
                return;
            }
            ICollection<FileInfo> filesToCompress = FindFilesToCompress(archiveDirectory, archiveFilePattern);
            if(filesToCompress.Count<FilesThreshold)
            {
                _compressingStarted = 0;
                return;
            }
            Task compressTask = new Task(CompressFiles,filesToCompress,TaskCreationOptions.LongRunning);
            compressTask.Start();
        }

        /// <summary>
        /// Method find files in archive directory which already rolled by date
        /// and appropriate for compressing to ZIP archive
        /// </summary>
        /// <returns>File info collection</returns>
        protected virtual ICollection<FileInfo> FindFilesToCompress(String archiveDirectory,String archiveFilePattern)
        {
            if (String.IsNullOrWhiteSpace(archiveDirectory))
            {
                return new List<FileInfo>(0);
            }
            Int32 patternStart = archiveFilePattern.IndexOf('{');
            Int32 patternEnd = archiveFilePattern.IndexOf('}');
            if(patternStart==-1 || patternEnd==-1 || patternEnd<patternStart)
            {
                InternalLogger.Error("Archive file name patter has invalid format '{}'", archiveFilePattern);
                return new List<FileInfo>();
            }
            String archiveFileNameBase = Path.GetFileNameWithoutExtension(archiveFilePattern)
                                                .Remove(patternStart,patternEnd-patternStart+1);
            String archiveExtension = Path.GetExtension(archiveFilePattern);
            if (String.IsNullOrWhiteSpace(archiveExtension))
            {
                InternalLogger.Error("Archive file name extension missed, '{}'", archiveFilePattern);
                return new List<FileInfo>();
            }

            DirectoryInfo archiveDirectoryInfo = new DirectoryInfo(archiveDirectory);
            ICollection<FileInfo> zipFiles = new List<FileInfo>();
            Regex fileMatcher = new Regex(archiveFileNameBase.Insert(patternStart,@"[\w\W]*"));
            foreach (FileInfo fileInfo in archiveDirectoryInfo.GetFiles())
            {
                if ( fileMatcher.IsMatch(fileInfo.Name) 
                    && String.Equals(fileInfo.Extension,archiveExtension,StringComparison.OrdinalIgnoreCase))
                {
                    zipFiles.Add(fileInfo);
                }
            }
            return zipFiles;
        }

        /// <summary>
        /// Parse ZipFile property and get real file name for compressed logs
        /// </summary>
        /// <returns></returns>
        private String GetCompressedFileName()
        {
            //replace datetime pattern with current timestamp
            return _datePatternRegex.Replace(ZipFile, DateTime.Now.ToString(_zipDateFormat, CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Compress passed files to ZIP archive
        /// </summary>
        /// <param name="state">ICollection<FileInfo> object with list of files to compress</param>
        private void CompressFiles(Object state)
        {
            ICollection<FileInfo> filesToZip = (ICollection<FileInfo>)state;
            ZipFile compressedBackups;
            String zipFileName = GetCompressedFileName();
            try
            {
                if (File.Exists(zipFileName))
                {
                    String zipDir = Path.GetDirectoryName(zipFileName);
                    String zipFileNameNoExt = Path.GetFileNameWithoutExtension(zipFileName);
                    String newZipName = zipFileNameNoExt+"_" + DateTime.Now.Ticks.ToString() + Path.GetExtension(zipFileName);
                    zipFileName = Path.Combine(zipDir,newZipName);
                }
                compressedBackups = new ZipFile(zipFileName)
                {
                    CompressionLevel = CompressionLevel,
                    CompressionMethod = CompressionMethod.Deflate,
                    UseZip64WhenSaving = Zip64Option.AsNecessary
                };
            }
            catch (Exception ex)
            {
                _compressingStarted = 0;
                InternalLogger.Error("Error while creating zip file={0}{1}{2}", zipFileName, Environment.NewLine, ex.ToString());
                return;
            }

            foreach (FileInfo logFile in filesToZip)
            {
                try
                {
                    Stream fileToZip = new FileStream(logFile.FullName, FileMode.Open);
                    compressedBackups.AddEntry(logFile.Name, fileToZip);
                }
                catch (Exception ex)
                {
                    InternalLogger.Error("Error while open file stream for file {0} for zip archive{1}{2}", 
                                            logFile.FullName, 
                                            Environment.NewLine, 
                                            ex.ToString());
                }
            }
            //save zip archive and delete old log files
            try
            {
                compressedBackups.Save();
                foreach (FileInfo fileForZip in filesToZip)
                {
                    try
                    {
                        File.Delete(fileForZip.FullName);
                    }
                    catch (Exception ex)
                    {
                        InternalLogger.Error("Error while deleting zipped log file {0}{1}{2}", 
                                                fileForZip.FullName, 
                                                Environment.NewLine, 
                                                ex.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error("Error while zipping files{0}{1}", 
                                        Environment.NewLine, 
                                        ex.ToString());
            }
            finally
            {
                _compressingStarted = 0;
                compressedBackups.Dispose();
            }
        }
    }
}
