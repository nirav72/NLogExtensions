NLogExtensions
==============

Project maintains different extensions for NLog library.

Project contains following extension targets:
<ul>
 <li><b>ArchiveFilesTarget</b><br/>
    <p>
      Target for automatic compressing of log files. <br/>
      It detect archived files in Path.GetDirectoryName(ArchiveFileName) directory, compare count of detected files with FilesThreshold property. If FilesThreshold is exceeded, found archive files will compressed to ZIP archive by using Deflate algorithm. Compression quality may be tuned by using CompressionLevel property. Class use DotNetZip library for compression purpose.
      Current target extends NLog FileTarget class,so all it's settings available.
      Additional settings of ArchiveFilesTarget:
      <ol>
        <li>FilesThreshold - How many files have to be rolled before compressing started. Default value is 1.</li>
        <li>CompressionLevel - compression level for backup archiving(Level0,Level9,BestSpeed,BestCompression and etc). For full list of possible values,see documentation of enum CompressionLevel in  DotNetZip library.Default value is Level9.</li>
        <li>ZipFile [Required parameter] - Path in which ZIP archive will created. ZIP file name may DateTime pattern which will be replaced by DateTime.Now(when this ZIP archive was created). DateTime pattern MUST be decorated by bracket,for instance {yyyy-MM-dd}.</li>
      </ol>
      <b>Notes: </b>If ArchiveFilesTarget detect ZIP archive with name which currently exists in ZipFile folder, it adds to name random suffix to prevent file overwriting.
    </p>
   <p>
    Example configuration:
      
      <configuration>
      <configSections>
        <section name="nlog" type="NLog.Config.ConfigSectionHandler, NLog"/>
      </configSections>
      <nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
        <extensions>
          <add assembly="NLogExtensions.Targets"/>
        </extensions>
        <targets>
          <target xsi:type="AsyncWrapper" name="async" queueLimit="500" timeToSleepBetweenBatches="15" batchSize="20" overflowAction="Block">
            <target name="file"
                    xsi:type="ArchiveFiles"
                    layout="${longdate} [${level:uppercase=true}] [ThreadID=${threadid}] ${newline}${message}${newline}"
                    fileName="C:\Logs\test.log"
                    archiveNumbering="Sequence"
                    archiveEvery="Minute"
                    archiveFileName="C:\Logs\testArchive[{########}].log"
                    filesthreshold="2"
                    compressionlevel="Level9"
                    zipfile="C:\Logs\test[{yyyy-MM-dd}].zip"
                    maxArchiveFiles="5"
                    concurrentWrites="false"
                    concurrentWriteAttemptDelay="20"
                    concurrentWriteAttempts="20"
                    bufferSize ="16384"
                    autoFlush="false"
                    keepFileOpen="true"
                    lineEnding="Default"/>
          </target>
        </targets>
        <rules>
          <logger name="commonLog" minlevel="Debug" writeTo="async" />
        </rules>
      </nlog>
    </configuration>
    </p>
  </li>
<ul>
