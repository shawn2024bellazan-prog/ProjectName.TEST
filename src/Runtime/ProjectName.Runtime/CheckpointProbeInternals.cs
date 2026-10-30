using System.Runtime.CompilerServices;

// Development test process only. The AppHost does not reference the probe.
[assembly: InternalsVisibleTo("ProjectName.CheckpointProbe")]
