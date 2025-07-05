using Microsoft.Extensions.Logging;
using System.IO.Abstractions;
using System.Text.Json;

namespace Pico4SAFTExtTrackingModule.PicoConnectors.ConfigChecker.PicoConnect;

public sealed class PicoConnectConfigChecker : IConfigChecker
{
    private readonly ILogger logger;
    private readonly IFileSystem fileSystem;

    public PicoConnectConfigChecker(ILogger logger, IFileSystem fileSystem)
    {
        this.logger = logger;
        this.fileSystem = fileSystem;
        this.picoConfig = new Lazy<Config>(() => GetConfig(fileSystem, logger));
    }

    public PicoConnectConfigChecker(ILogger logger) : this(logger, new FileSystem()) { }

    private static string GetProgramFsBasePath(PicoPrograms program)
    {
        switch (program)
        {
            case PicoPrograms.BusinessStreaming:
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Business Streaming");

            case PicoPrograms.PicoConnect:
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PICO Connect");

            default:
                // shouldn't reach this
                return "";
        }
    }

    private static Config GetConfig(IFileSystem fileSystem, PicoPrograms program, ILogger? logger = null)
    {
        string configLocation = Path.Combine(GetProgramFsBasePath(program), "settings.json");
        logger.LogInformation("Expecting PICO settings file at '" + configLocation + "'");
        try
        {
            string configContents = fileSystem.File.ReadAllText(configLocation);
            return JsonSerializer.Deserialize<Config>(configContents);
        }
        catch (Exception ex)
        {
            logger?.LogError("Pico Config deserialize failed: " + ex.ToString());
            return null;
        }
    }

    public int GetTransferProtocolNumber(PicoPrograms program)
    {
        if (program != PicoPrograms.PicoConnect && program != PicoPrograms.BusinessStreaming)
            throw new ArgumentException("PicoConnectConfigChecker class only checks for PICO Connect or Business Streaming 2.0 config files");

        Config picoConfig = GetConfig(this.fileSystem, program, this.logger);
        if (picoConfig == null || picoConfig!.lab == null)
        {
            logger.LogError("Couldn't get the value of `faceTrackingTransferProtocol` on the setting.json file");
            return 0; // send default value
        }

        return picoConfig!.lab!.faceTrackingTransferProtocol;
    }
}