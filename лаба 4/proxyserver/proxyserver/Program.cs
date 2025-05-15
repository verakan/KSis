using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace ProxyServer
{
    class Program
    {
        // Флаги и настройки сервера
        public static bool IsRunning = true;
        public const int BUFFER_SIZE = 8192;
        public static IPAddress ListenIp = IPAddress.Parse("127.0.0.1");
        public const int Port = 8888;

        public static void Main(string[] args)
        {
            var listener = new TcpListener(ListenIp, Port);
            listener.Start();
            Console.WriteLine($"Прокси-сервер запущен на {ListenIp}:{Port}");

            try
            {
                while (IsRunning)
                {
                    var client = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(HandleClient, client);
                }
            }
            catch (Exception ex) when (IsRunning)
            {
                Console.WriteLine($"Ошибка сервера: {ex.Message}");
            }
            finally
            {
                listener.Stop();
            }
        }

        static void HandleClient(object state)
        {
            var client = (TcpClient)state;

            try
            {
                using (client)
                using (var clientStream = client.GetStream())
                {
                    byte[] httpRequest = Receive(clientStream);
                    if (httpRequest.Length == 0) return;

                    string fullRequest = Encoding.UTF8.GetString(httpRequest);

                    string host;
                    IPEndPoint remoteEndPoint = GetRemoteEndpoint(fullRequest, out host);

                    if (fullRequest.StartsWith("GET http", StringComparison.OrdinalIgnoreCase) ||
                        fullRequest.StartsWith("POST http", StringComparison.OrdinalIgnoreCase))
                    {
                        fullRequest = GetRelativePath(fullRequest);
                    }

                    byte[] fixedRequestBytes = Encoding.UTF8.GetBytes(fullRequest);
                    ProcessRequest(clientStream, fixedRequestBytes, remoteEndPoint, host);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при обработке клиента: {ex.Message}");
            }
            finally
            {
                // Закрываем соединение с клиентом
                client.Close();
            }
        }


        /// <summary>
        /// Синхронное чтение входящего потока
        /// </summary>
        static byte[] Receive(NetworkStream stream)
        {
            byte[] buffer = new byte[BUFFER_SIZE];
            using var memoryStream = new System.IO.MemoryStream();

            int bytesRead;
            do
            {
                bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead > 0)
                {
                    memoryStream.Write(buffer, 0, bytesRead);
                }
            } while (stream.DataAvailable && bytesRead > 0);

            return memoryStream.ToArray();
        }

        /// <summary>
        /// Преобразует абсолютный URL в относительный путь
        /// </summary>
        static string GetRelativePath(string request)
        {
            Regex regex = new Regex(@"http:\/\/[a-z0-9а-я\.\:]*", RegexOptions.IgnoreCase);
            return regex.Replace(request, "");
        }

        /// <summary>
        /// Синхронно определяет удалённую конечную точку из HTTP-запроса
        /// </summary>
        static IPEndPoint GetRemoteEndpoint(string request, out string host)
        {
            Regex regex = new Regex(@"Host:\s*(?<host>[^:\r\n]+)(:(?<port>\d+))?", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            Match match = regex.Match(request);
            host = match.Groups["host"].Value;
            int port = 80;

            if (!string.IsNullOrEmpty(match.Groups["port"].Value))
            {
                int.TryParse(match.Groups["port"].Value, out port);
            }

            IPAddress[] addresses;
            try
            {
                addresses = Dns.GetHostAddresses(host);
            }
            catch
            {
                addresses = new IPAddress[] { IPAddress.Loopback };
            }

            IPAddress ipAddress = addresses.Length > 0 ? addresses[0] : IPAddress.Loopback;
            return new IPEndPoint(ipAddress, port);
        }

        /// <summary>
        /// Синхронно обрабатывает запрос и передаёт данные между клиентом и сервером
        /// </summary>
        static void ProcessRequest(NetworkStream clientStream, byte[] requestBytes, IPEndPoint remoteEP, string host)
        {
            try
            {
                using (var serverClient = new TcpClient())
                {
                    serverClient.Connect(remoteEP.Address, remoteEP.Port);
                    using (var serverStream = serverClient.GetStream())
                    {
                        // Отправляем запрос на удалённый сервер
                        serverStream.Write(requestBytes, 0, requestBytes.Length);
                        serverStream.Flush();

                        byte[] buffer = new byte[BUFFER_SIZE];
                        int bytesRead;
                        bool headerParsed = false;
                        List<byte> headerAccumulator = new List<byte>();
                        string statusLine = "";
                        bool logEveryChunk = false;

                        // Читаем ответ от сервера и передаём клиенту
                        while ((bytesRead = serverStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            clientStream.Write(buffer, 0, bytesRead);
                            clientStream.Flush();

                            // Обработка заголовка
                            if (!headerParsed)
                            {
                                headerAccumulator.AddRange(buffer.Take(bytesRead));
                                string headerText = Encoding.UTF8.GetString(headerAccumulator.ToArray());
                                int headerEnd = headerText.IndexOf("\r\n\r\n");

                                if (headerEnd != -1)
                                {
                                    string headerSection = headerText.Substring(0, headerEnd);
                                    string[] headerLines = headerSection.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                                    if (headerLines.Length > 0)
                                    {
                                        statusLine = headerLines[0];
                                    }
                                    headerParsed = true;

                                    // Логирование для аудио потоков
                                    if (headerText.ToLower().Contains("content-type:") &&
                                        headerText.ToLower().Contains("audio"))
                                    {
                                        logEveryChunk = true;
                                    }
                                    Console.WriteLine($"{DateTime.Now:dd.MM.yyyy HH:mm:ss} {host} {statusLine}");

                                }
                            }
                            else if (logEveryChunk)
                            {
                                Console.WriteLine($"{DateTime.Now} {host} {statusLine}");
                            }
                        }
                    }
                }
            }

            catch (Exception ex)
            {
            }

        }
    }
}