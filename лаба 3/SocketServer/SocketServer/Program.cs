using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SocketServer
{
    class Server
    {
        private static Socket _serverSocket;
        private static List<Socket> _clientSockets = new List<Socket>();
        private static Dictionary<Socket, string> _clientUsernames = new Dictionary<Socket, string>();
        private static bool _isRunning = true;
        private static readonly object _lock = new object();

        static void Main(string[] args)
        {
            Console.Title = "Сервер";
            Console.OutputEncoding = Encoding.UTF8;

            try
            {
                StartServer();
                Console.WriteLine("Сервер остановлен. Нажмите любую клавишу для выхода...");
                Console.ReadKey();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка сервера: {ex.Message}");
            }
        }


        private static void StartServer()
        {
            Console.Write("Введите IP сервера: ");
            string ipString = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(ipString))
            {
                ipString = "127.0.0.1";
            }

            if (!IPAddress.TryParse(ipString, out IPAddress serverIp))
            {
                Console.WriteLine("Недопустимый формат IP-адреса");
                return;
            }

            Console.Write("Введите номер порта сервера: ");
            int port = int.TryParse(Console.ReadLine(), out int p) ? p : 8888;

            // Проверка доступности порта
            if (IsPortInUse(serverIp, port))
            {
                Console.WriteLine($"Не удается запустить сервер: порт {port} на IP {serverIp} уже используется");
                Console.WriteLine("Пожалуйста, выберите другой IP-адрес или номер порта");
                return;
            }

            _serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                _serverSocket.Bind(new IPEndPoint(serverIp, port));
                _serverSocket.Listen(5);
                Console.WriteLine($"Сервер успешно запущен на {serverIp}:{port}");
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"Не удалось запустить сервер: {ex.Message}");
                if (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
                {
                    Console.WriteLine("Указанная комбинация IP-адреса и порта уже используется");
                }
                return;
            }

            Thread acceptThread = new Thread(AcceptConnections);
            acceptThread.Start();

            Console.WriteLine("Введите '/stop' чтобы выключить сервер");
            while (_isRunning)
            {
                string input = Console.ReadLine();
                if (input == "/stop")
                {
                    _isRunning = false;
                    _serverSocket.Close();
                    break;
                }
            }
        }

        // Метод для проверки занятости порта
        private static bool IsPortInUse(IPAddress ip, int port)
        {
            try
            {
                using (var tester = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                {
                    tester.Bind(new IPEndPoint(ip, port));
                    return false;
                }
            }
            catch (SocketException)
            {
                return true;
            }
        }

        private static void AcceptConnections()
        {
            try
            {
                while (_isRunning)
                {
                    Socket clientSocket = _serverSocket.Accept();
                    lock (_lock) // Блокировка при добавлении нового клиента
                    {
                        _clientSockets.Add(clientSocket);
                    }

                    IPEndPoint clientEndPoint = (IPEndPoint)clientSocket.RemoteEndPoint;
                    Console.WriteLine($"Клиент подключён: {clientEndPoint.Address}");

                    Thread clientThread = new Thread(() => HandleClient(clientSocket));
                    clientThread.Start();
                }
            }
            catch (SocketException) when (!_isRunning)
            {
                // Нормальное завершение работы
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Принять ошибку: {ex.Message}");
            }
            finally
            {
                Cleanup();
            }
        }

        private static void HandleClient(Socket clientSocket)
        {
            try
            {
                byte[] buffer = new byte[2048];
                string username = null;
                IPEndPoint clientEndPoint = (IPEndPoint)clientSocket.RemoteEndPoint;
                string clientInfo = $"{clientEndPoint.Address}";

                while (_isRunning)
                {
                    int bytesReceived = clientSocket.Receive(buffer);
                    if (bytesReceived == 0) break;

                    string message = Encoding.UTF8.GetString(buffer, 0, bytesReceived);

                    if (message.StartsWith("USERNAME:"))
                    {
                        username = message.Substring(9);
                        lock (_lock)
                        {
                            _clientUsernames[clientSocket] = username;
                        }
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {username} ({clientInfo}) присоединился");
                        BroadcastSystemMessage($"{username} присоединился к чату");
                    }
                    else if (message.StartsWith("/exit:"))
                    {
                        string exitUsername = message.Substring(6);
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {exitUsername} ({clientInfo}) отключился");
                        BroadcastSystemMessage($"{exitUsername} вышел из чата");
                        break;
                    }
                    else
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {username}: {message}");
                        BroadcastUserMessage(username, message);
                    }
                }
            }
            catch (SocketException)
            {
                // Клиент отключился
            }

            finally
            {
                string disconnectedUsername;
                lock (_lock)
                {
                    if (_clientUsernames.TryGetValue(clientSocket, out disconnectedUsername))
                    {
                        _clientUsernames.Remove(clientSocket);
                        // Убираем дублирующее сообщение
                        BroadcastSystemMessage($"{disconnectedUsername} вышел из чата");
                    }
                    _clientSockets.Remove(clientSocket);
                }
                clientSocket.Close();
            }
        }

        

        private static void BroadcastUserMessage(string username, string message)
        {
            string formattedMessage = $"[{DateTime.Now:HH:mm:ss}] {username}: {message}";
            byte[] data = Encoding.UTF8.GetBytes(formattedMessage);
            SendToAllClients(data);
        }

        private static void BroadcastSystemMessage(string message)
        {
            string formattedMessage = $"[{DateTime.Now:HH:mm:ss}] СЕРВЕР: {message}";
            byte[] data = Encoding.UTF8.GetBytes(formattedMessage);
            SendToAllClients(data);
        }

        private static void SendToAllClients(byte[] data)
        {
            lock (_lock)
            {
                foreach (var client in _clientSockets.ToArray())
                {
                    try
                    {
                        if (client.Connected)
                        {
                            client.Send(data);
                        }
                    }
                    catch
                    {
                        try { client.Close(); } catch { }
                        _clientSockets.Remove(client);
                        if (_clientUsernames.ContainsKey(client))
                        {
                            _clientUsernames.Remove(client);
                        }
                    }
                }
            }
        }

        private static void Cleanup()
        {
            lock (_lock)
            {
                foreach (var client in _clientSockets.ToArray())
                {
                    try
                    {
                        client.Shutdown(SocketShutdown.Both);
                        client.Close();
                    }
                    catch { }
                }
                _clientSockets.Clear();
                _clientUsernames.Clear();
            }

            try
            {
                _serverSocket?.Close();
            }
            catch { }
        }
    }
}