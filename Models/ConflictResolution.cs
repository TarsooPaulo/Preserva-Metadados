namespace PreservaMetadados.Models;

/// <summary>
/// Define as estratégias de resolução de conflitos durante a transferência.
/// </summary>
public enum ConflictResolution
{
    /// <summary>
    /// Perguntar ao usuário o que fazer (padrão)
    /// </summary>
    Ask,
    
    /// <summary>
    /// Sobrescrever o arquivo de destino se for mais antigo
    /// </summary>
    OverwriteIfOlder,
    
    /// <summary>
    /// Sobrescrever sempre o arquivo de destino
    /// </summary>
    OverwriteAlways,
    
    /// <summary>
    /// Ignorar o arquivo conflitante
    /// </summary>
    Ignore,
    
    /// <summary>
    /// Renomear o arquivo de destino (adicionar sufixo)
    /// </summary>
    Rename
}

/// <summary>
/// Configurações de resolução de conflitos
/// </summary>
public class ConflictResolutionSettings
{
    public ConflictResolution DefaultResolution { get; set; } = ConflictResolution.Ask;
    public bool ApplyToAll { get; set; } = false;
    public string RenameSuffix { get; set; } = "_copy";
    public bool SkipHiddenFiles { get; set; } = false;
    public bool SkipSystemFiles { get; set; } = false;
    
    public ConflictResolutionSettings Clone()
    {
        return new ConflictResolutionSettings
        {
            DefaultResolution = DefaultResolution,
            ApplyToAll = ApplyToAll,
            RenameSuffix = RenameSuffix,
            SkipHiddenFiles = SkipHiddenFiles,
            SkipSystemFiles = SkipSystemFiles
        };
    }
}