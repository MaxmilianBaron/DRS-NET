using DungeonRunners.Engine;
using DungeonRunners.Networking;

namespace DungeonRunners.Core
{
    public class ServerHost : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private ServerConfig serverConfig;

        [Header("Server Components")]
        [SerializeField] private bool startAuthServer = true;
        [SerializeField] private bool startGameServer = true;

        private AuthServer _authServer;
        private GameServer _gameServer;

        void Awake()
        {
            RuntimeEvidence.EnsureStarted();
            if (RuntimeEvidence.ShouldAbortStartup)
            {
                enabled = false;
                Application.Quit();
                return;
            }
            var dispatcher = MainThreadDispatcher.Instance;
        }

        void Start()
        {
            if (RuntimeEvidence.ShouldAbortStartup)
                return;

            if (serverConfig == null)
            {
                Debug.LogError("[SERVER] config state=missing");
                return;
            }

            Debug.Log("[SERVER] state=starting");
            RuntimeEvidence.LogBuildBinding("startup");
            Debug.Log($"[SERVER] version={serverConfig.serverVersion}");
            Debug.Log($"[SERVER] maxPlayers={ServerSettings.Get("maxPlayers", serverConfig.maxPlayers)}");

            if (startAuthServer)
            {
                StartAuthServer();
            }

            if (startGameServer)
            {
                StartGameServer();
            }

            Debug.Log("[SERVER] state=initialized");
        }

        private void StartAuthServer()
        {
            GameObject authGO = new GameObject("AuthServer");
            authGO.transform.SetParent(transform);
            _authServer = authGO.AddComponent<AuthServer>();

            var field = typeof(AuthServer).GetField("config",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(_authServer, serverConfig);

            Debug.Log($"[SERVER] component=AuthServer port={serverConfig.authServerPort} state=created");
        }

        private void StartGameServer()
        {
            GameObject gameGO = new GameObject("GameServer");
            gameGO.transform.SetParent(transform);
            _gameServer = gameGO.AddComponent<GameServer>();

            var field = typeof(GameServer).GetField("config",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(_gameServer, serverConfig);

            Debug.Log($"[SERVER] component=GameServer port={serverConfig.gameServerPort} state=created");
        }

        void OnApplicationQuit()
        {
            Debug.Log("[SERVER] state=shutdown");
        }
    }
}
