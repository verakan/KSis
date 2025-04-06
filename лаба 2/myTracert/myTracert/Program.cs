using System;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;

class Tracert
{

    private const int timeout = 1000; // Таймаут ожидания ответа (мс)
    private const int maxHops = 30; // Максимальное количество прыжков
    private const int bufferSize = 1024; // Размер буфера для приема данных
    private const int packetsPerHop = 3; // Количество пакетов на каждый хоп
    private static ushort sequenceNumber = 0; // Счетчик последовательности пакетов

    static void Main(string[] args)
    {
        // Проверка наличия аргумента командной строки
        if (args.Length == 0)
        {
            Console.WriteLine("myTracert <адрес>");
            return;
        }

        string target = args[0];  // Целевой адрес из аргументов
        IPAddress targetAddress;   // IP-адрес цели

        try
        {
            // Получаем все IP-адреса для целевого имени
            IPAddress[] addresses = Dns.GetHostAddresses(target);
            if (addresses.Length == 0)
            {
                Console.WriteLine("Не удалось выполнить разрешение имени");
                return;
            }
            targetAddress = addresses[0];  // Берем первый адрес
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при разрешении имени: {ex.Message}");
            return;
        }

        // Выводим информацию о начале трассировки
        Console.WriteLine($"Трассировка маршрута к {target} [{targetAddress}] с максимальным числом хопов {maxHops}:");

        // Создаем raw-сокет для работы с ICMP
        using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Icmp))
        {
            // Привязываем сокет к любому доступному IP и порту
            socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            socket.ReceiveTimeout = timeout;  // Устанавливаем таймаут

            // Основной цикл по количеству прыжков (TTL)
            for (int ttl = 1; ttl <= maxHops; ttl++)
            {
                // Устанавливаем TTL для текущего прыжка
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.IpTimeToLive, ttl);

                Console.Write($"{ttl}\t");  // Выводим номер текущего прыжка

                IPAddress remoteAddress = null; // Адрес ответившего узла
                bool destinationReached = false; // Флаг достижения цели
                int successfulResponses = 0; // Счетчик успешных ответов
                long totalTime = 0; // Суммарное время ответов

                // Отправляем несколько пакетов для текущего TTL
                for (int packetNumber = 0; packetNumber < packetsPerHop; packetNumber++)
                {
                    byte[] sendBuffer = CreateIcmpPacket(); // Создаем ICMP-пакет
                    byte[] receiveBuffer = new byte[bufferSize]; // Буфер для ответа

                    // Конечная точка для приема ответа
                    IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    EndPoint remoteEP = (EndPoint)remoteEndPoint;

                    // Замеряем время отправки-приема
                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();
                    // Отправляем пакет на целевой адрес
                    socket.SendTo(sendBuffer, new IPEndPoint(targetAddress, 0));

                    int bytesReceived = 0;
                    try
                    {
                        // Пытаемся получить ответ
                        bytesReceived = socket.ReceiveFrom(receiveBuffer, ref remoteEP);
                        stopwatch.Stop();

                        // Получаем адрес ответившего узла
                        remoteAddress = ((IPEndPoint)remoteEP).Address;
                        long roundTripTime = stopwatch.ElapsedMilliseconds;
                        Console.Write($"{roundTripTime} ms\t");  // Время ответа
                        totalTime += roundTripTime;
                        successfulResponses++;

                        // Проверяем, достигли ли целевого адреса
                        if (remoteAddress.Equals(targetAddress))
                        {
                            destinationReached = true;
                        }
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                    {
                        Console.Write("*\t");  // Таймаут - нет ответа
                    }
                    catch (SocketException ex)
                    {
                        Console.Write($"{ex.SocketErrorCode}\t");  // Другие ошибки сокета
                    }

                    sequenceNumber++;  // Увеличиваем номер последовательности
                }

                // Выводим информацию об узле
                if (remoteAddress != null)
                {
                    try
                    {
                        // Пытаемся получить имя хоста
                        IPHostEntry hostEntry = Dns.GetHostEntry(remoteAddress);
                        Console.Write($"{hostEntry.HostName} [{remoteAddress}]");
                    }
                    catch
                    {
                        Console.Write($"{remoteAddress}");  // Если не удалось получить имя
                    }

                    // Выводим среднее время ответа
                    if (successfulResponses > 0)
                    {
                        Console.Write($"\tСреднее: {totalTime / successfulResponses} ms");
                    }
                }

                Console.WriteLine();  // Переход на новую строку

                // Если достигли цели - завершаем трассировку
                if (destinationReached)
                {
                    Console.WriteLine("Трассировка завершена.");
                    return;
                }
            }
        }
    }

    // Метод создания ICMP-пакета (Echo Request)
    private static byte[] CreateIcmpPacket()
    {
        byte[] packet = new byte[64];  // Стандартный размер ICMP-пакета

        // Заполняем заголовок ICMP-пакета:
        packet[0] = 8;  // Type = 8 (Echo Request)
        packet[1] = 0;  // Code = 0
        packet[2] = 0; 
        packet[3] = 0;  
        packet[4] = 0;  
        packet[5] = 0;  

        packet[6] = (byte)(sequenceNumber >> 8);   // Старший байт
        packet[7] = (byte)(sequenceNumber & 0xFF); // Младший байт

        // Заполняем данные пакета (просто последовательность байт)
        for (int i = 8; i < packet.Length; i++)
        {
            packet[i] = (byte)i;
        }

        // Вычисляем и устанавливаем контрольную сумму
        ushort checksum = CalculateChecksum(packet);
        packet[2] = (byte)(checksum >> 8);    
        packet[3] = (byte)(checksum & 0xFF);  

        return packet;
    }

    // Метод вычисления контрольной суммы ICMP-пакета
    private static ushort CalculateChecksum(byte[] buffer)
    {
        uint sum = 0;  

        // Суммируем все 16-битные слова
        for (int i = 0; i < buffer.Length; i += 2)
        {
            if (i + 1 < buffer.Length)
            {
                // Складываем два байта как 16-битное слово
                sum += (ushort)((buffer[i] << 8) + buffer[i + 1]);
            }
            else
            {
                // Если нечетное число байт, добавляем последний байт с нулевым младшим байтом
                sum += (ushort)(buffer[i] << 8);
            }
        }

        // Складываем все переполнения (старшие биты) с младшими
        while ((sum >> 16) != 0)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        // Инвертируем результат и возвращаем как 16-битное число
        return (ushort)(~sum);
    }
}