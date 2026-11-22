using System.Runtime.CompilerServices;

// Test-only terminal-handler injection is not a public live-transport construction path.
[assembly: InternalsVisibleTo("HuayuanMusic.Kugou.GatewayProbe")]

// The compiled trusted host owns response-stream lifetime; terminal injection is used only by its self-test.
[assembly: InternalsVisibleTo("HuayuanMusic.P17.Nightflight")]
