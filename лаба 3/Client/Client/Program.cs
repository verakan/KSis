using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SocketClient
{
    class Client
    {
        private static Socket _clientSocket;
        private static string _username;
        private static bool _isRunning = true;
        private static string _serverIp;
        private static string _clientIp;
        private static int _port;

        static void Main(string[] args)
        {
            Console.Title = "Клиент";
            Console.OutputEncoding = Encoding.UTF8;

            try
            {
                // Обработка аргументов командной строки
                if (args.Length > 0)
                {
                    ParseCommandLineArgs(args);
                }
                else
                {
                    InitializeClient();
                }

                ConnectToServer();
                StartReceivingMessages();
                SendMessages();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
            }
            finally
            {
                Cleanup();
            }
        }

        private static void ParseCommandLineArgs(string[] args)
        {

            _username = args[0];
            _serverIp = args[1];
            _clientIp = args[2];

            if (!IPAddress.TryParse(_serverIp, out _) || !IPAddress.TryParse(_clientIp, out _))
            {
                Console.WriteLine("Недопустимый формат IP-адреса");
                Environment.Exit(1);
            }

            if (!int.TryParse(args[3], out _port) || _port <= 0 || _port > 65535)
            {
                Console.WriteLine("Недопустимый номер порта");
                Environment.Exit(1);
            }

            _clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            Console.WriteLine($"Имя пользователя: {_username}");
            Console.WriteLine($"Сервер: {_serverIp}:{_port}");
            Console.WriteLine($"IP клиента: {_clientIp}");
        }

        private static void InitializeClient()
        {
            // Ввод имени пользователя
            do
            {
                Console.Write("Введите имя пользователя: ");
                _username = Console.ReadLine();
            } while (string.IsNullOrWhiteSpace(_username));

            // Ввод IP-адреса сервера с проверкой
            bool validServerIp = false;
            do
            {
                Console.Write("Введите IP сервера: ");
                string inputIp = Console.ReadLine();
                _serverIp = string.IsNullOrWhiteSpace(inputIp) ? "127.0.0.1" : inputIp;

                if (IPAddress.TryParse(_serverIp, out _))
                {
                    validServerIp = true;
                }
                else
                {
                    Console.WriteLine("Неверный формат IP-адреса сервера. Пожалуйста, попробуйте снова.");
                }
            } while (!validServerIp);

            // Ввод IP-адреса клиента с проверкой
            bool validClientIp = false;
            do
            {
                Console.Write("Введите IP клиента: ");
                string inputIp = Console.ReadLine();
                _clientIp = string.IsNullOrWhiteSpace(inputIp) ? "127.0.0.1" : inputIp;

                if (IPAddress.TryParse(_clientIp, out _))
                {
                    validClientIp = true;
                }
                else
                {
                    Console.WriteLine("Недопустимый формат IP-адреса клиента. Пожалуйста, попробуйте снова.");
                }
            } while (!validClientIp);

            // Ввод порта с проверкой
            bool validPort = false;
            do
            {
                Console.Write("Введите номер порта: ");
                string portInput = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(portInput))
                {
                    _port = 8888;
                    validPort = true;
                }
                else if (int.TryParse(portInput, out _port) && _port > 0 && _port < 65536)
                {
                    validPort = true;
                }
                else
                {
                    Console.WriteLine("Недопустимый порт. Значение порта должно быть от 1 до 65535.");
                }
            } while (!validPort);

            _clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        }

        private static void ConnectToServer()
        {
            try
            {
                // Привязываем сокет к указанному клиентскому IP
                IPEndPoint clientEndPoint = new IPEndPoint(IPAddress.Parse(_clientIp), 0);
                _clientSocket.Bind(clientEndPoint);

                Console.WriteLine($"\nКлиент с адресом {_clientIp}");
                Console.WriteLine($"Подключение к серверу с адресом {_serverIp}:{_port}...");

                _clientSocket.Connect(IPAddress.Parse(_serverIp), _port);
                Console.WriteLine("Подключен к серверу!");

                byte[] usernameData = Encoding.UTF8.GetBytes($"USERNAME:{_username}");
                _clientSocket.Send(usernameData);

                Console.WriteLine($"\nДобро пожаловать, {_username}! Вы можете начать общение прямо сейчас.");
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"\nНе удалось установить соединение: {ex.Message}");
                _isRunning = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nОшибка: {ex.Message}");
                _isRunning = false;
            }
        }

        private static void StartReceivingMessages()
        {
            Thread receiveThread = new Thread(() =>
            {
                try
                {
                    byte[] buffer = new byte[2048];

                    while (_isRunning)
                    {
                        int bytesReceived = _clientSocket.Receive(buffer);
                        if (bytesReceived == 0)
                        {
                            Console.WriteLine("\nСервер отключен.");
                            _isRunning = false;
                            return;
                        }

                        string message = Encoding.UTF8.GetString(buffer, 0, bytesReceived);
                        DisplayMessage(message);
                    }
                }
                catch (SocketException)
                {
                    if (_isRunning)
                        Console.WriteLine("\nНеожиданная потеря соединения.");
                }
            });

            receiveThread.IsBackground = true;
            receiveThread.Start();
        }



        private static void SendMessages()
        {
            Console.WriteLine("\nВведите ваши сообщения ниже.");
            Console.WriteLine("Введите '/exit' чтобы отключиться от сервера");
            Console.WriteLine("------------------------------");

            while (_isRunning)
            {
                Console.Write("> ");
                string input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input)) continue;

                if (input.ToLower() == "/exit")
                {
                    try
                    {
                        byte[] exitData = Encoding.UTF8.GetBytes($"/exit:{_username}");
                        _clientSocket.Send(exitData);
                    }
                    catch { }
                    _isRunning = false;
                    Console.WriteLine("Отключение...");
                }
                else
                {
                    try
                    {
                        byte[] data = Encoding.UTF8.GetBytes(input);
                        _clientSocket.Send(data);
                    }
                    catch (SocketException)
                    {
                        Console.WriteLine("Не удалось отправить сообщение - соединение потеряно");
                        _isRunning = false;
                    }
                }
            }
        }

        private static void DisplayMessage(string message)
        {
            if (message.StartsWith("Сервер:"))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(message);
                Console.ResetColor();
            }
            else if (message.StartsWith(_username + ":"))
            {
                string msgWithoutUsername = message.Substring(_username.Length + 1);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"{_username}:{msgWithoutUsername}");
                Console.ResetColor();
            }
            /*else if (message.StartsWith("USER_LEFT:"))
            {
                string username = message.Substring("USER_LEFT:".Length);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"{username} has left the chat");
                Console.ResetColor();
            }*/
            else
            {
                // Для сообщений от других пользователей
                Console.WriteLine(message);
            }
        }

        private static void Cleanup()
        {
            _isRunning = false;

            try
            {
                if (_clientSocket != null)
                {
                    if (_clientSocket.Connected)
                    {
                        _clientSocket.Shutdown(SocketShutdown.Both);
                    }
                    _clientSocket.Close();
                }
            }
            catch { }

            Console.WriteLine("Клиент отключён. Нажмите любую клавишу для выхода...");
        }
    }
}