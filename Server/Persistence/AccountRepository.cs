using System;
using System.Security.Cryptography;
using System.Text;
using DungeonRunners.Engine;
using Mono.Data.Sqlite;

namespace DungeonRunners.Database
{
    public static class AccountRepository
    {
        public static uint CreateAccount(string username, string password)
        {
            Debug.LogError($"[DB-AUTH] CreateAccount called: username='{username}' password='{(string.IsNullOrEmpty(password) ? "EMPTY" : "***")}'");

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                Debug.LogError($"[DB-AUTH] REJECTED: username or password is empty!");
                return 0;
            }

            string salt = GenerateSalt();
            string hash = HashPassword(password, salt);

            try
            {
                Debug.LogError($"[DB-AUTH] DB Path: {GameDatabase.DbPath}");
                using (var connection = GameDatabase.GetConnection())
                {
                    Debug.LogError($"[DB-AUTH] Got connection, inserting account...");
                    GameDatabase.ExecuteNonQuery(connection,
                        "INSERT INTO accounts (username, password_hash, salt) VALUES (@u, @h, @s)",
                        ("@u", username), ("@h", hash), ("@s", salt));

                    Debug.LogError($"[DB-AUTH] INSERT done, getting ID...");
                    object accountIdValue = GameDatabase.ExecuteScalar(connection, "SELECT last_insert_rowid()");
                    uint accountId = Convert.ToUInt32(accountIdValue);
                    Debug.LogError($"[DB-AUTH] Created account '{username}' (ID: {accountId})");
                    return accountId;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DB-AUTH] CreateAccount FAILED: {ex.GetType().Name}: {ex.Message}");
                Debug.LogError($"[DB-AUTH] Stack: {ex.StackTrace}");

                if (ex.Message.Contains("UNIQUE") || ex.Message.Contains("unique"))
                    Debug.LogError($"[DB-AUTH] Username '{username}' already exists");

                return 0;
            }
        }

        public static uint Authenticate(string username, string password, bool autoCreate = true)
        {
            try
            {
                using (var connection = GameDatabase.GetConnection())
                {
                    using (var reader = GameDatabase.ExecuteReader(connection,
                        "SELECT id, password_hash, salt, is_banned FROM accounts WHERE username = @u",
                        ("@u", username)))
                    {
                        if (reader.Read())
                        {
                            uint accountId = (uint)reader.GetInt32(0);
                            string storedHash = reader.GetString(1);
                            string salt = reader.GetString(2);
                            bool isBanned = reader.GetInt32(3) != 0;

                            if (isBanned)
                            {
                                Debug.LogError($"[DB-AUTH] Account '{username}' is banned");
                                return 0;
                            }

                            string passwordHash = HashPassword(password, salt);
                            if (passwordHash != storedHash)
                            {
                                Debug.LogError($"[DB-AUTH] Wrong password for '{username}'");
                                return 0;
                            }

                            reader.Close();
                            GameDatabase.ExecuteNonQuery(connection,
                                "UPDATE accounts SET last_login = datetime('now') WHERE id = @id",
                                ("@id", (int)accountId));

                            Debug.LogError($"[DB-AUTH] Authenticated '{username}' (ID: {accountId})");
                            return accountId;
                        }
                    }

                    if (autoCreate)
                    {
                        Debug.LogError($"[DB-AUTH] Auto-creating account '{username}'");
                        return CreateAccount(username, password);
                    }

                    Debug.LogError($"[DB-AUTH] Account '{username}' not found");
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DB-AUTH] operation=Auth state=failed message='{ex.Message}'");
                return 0;
            }
        }

        public static bool UsernameExists(string username)
        {
            try
            {
                using (var connection = GameDatabase.GetConnection())
                {
                    object result = GameDatabase.ExecuteScalar(connection,
                        "SELECT COUNT(*) FROM accounts WHERE username = @u",
                        ("@u", username));
                    return Convert.ToInt32(result) > 0;
                }
            }
            catch { return false; }
        }

        public static uint GetAccountId(string username)
        {
            try
            {
                using (var connection = GameDatabase.GetConnection())
                {
                    object result = GameDatabase.ExecuteScalar(connection,
                        "SELECT id FROM accounts WHERE username = @u",
                        ("@u", username));
                    return result != null ? Convert.ToUInt32(result) : 0;
                }
            }
            catch { return 0; }
        }

        public static void SetBanned(uint accountId, bool banned)
        {
            try
            {
                using (var connection = GameDatabase.GetConnection())
                {
                    GameDatabase.ExecuteNonQuery(connection,
                        "UPDATE accounts SET is_banned = @b WHERE id = @id",
                        ("@b", banned ? 1 : 0), ("@id", (int)accountId));
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DB-AUTH] operation=Ban state=failed message='{ex.Message}'");
            }
        }


        private static string GenerateSalt()
        {
            byte[] saltBytes = new byte[16];
            using (var randomNumberGenerator = new RNGCryptoServiceProvider())
                randomNumberGenerator.GetBytes(saltBytes);
            return Convert.ToBase64String(saltBytes);
        }

        private static string HashPassword(string password, string salt)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(salt + password);
                byte[] hash = sha256.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }
    }
}
