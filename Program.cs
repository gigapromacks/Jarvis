using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using System.Text.Json;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Win32;
using NAudio.Wave;
using SkiaSharp;
using Vosk;

namespace gg
{
    class Program
    {public static bool IsFuck = false;


        // 👇 Добавьте этот импорт WinAPI в класс Program (рядом с SHEmptyRecycleBin)это для переключания окон
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        // Импорт функции SHEmptyRecycleBin
        [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
        static extern uint SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);

        // Флаги для очистки
        private const uint SHERB_NOCONFIRMATION = 0x00000001;
        private const uint SHERB_NOPROGRESSUI = 0x00000002;
        private const uint SHERB_NOSOUND = 0x00000004;

        // Проигрывание заранее записанного файла
        static async Task PlayVoiceAsync(string filePath)
        {
            try
            {
                using var audioFile = new AudioFileReader(filePath);
                using var outputDevice = new WaveOutEvent();
                outputDevice.Init(audioFile);
                outputDevice.Play();
                Console.WriteLine($"🔊 Проигрываю {filePath}...");

                while (outputDevice.PlaybackState == PlaybackState.Playing)
                {
                    await Task.Delay(100);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠ Ошибка при воспроизведении звука: {ex.Message}");
            }
        }
        public static void EmptyRecycleBin(bool showConfirmation = false)
        {
            try
            {
                uint flags = SHERB_NOPROGRESSUI | SHERB_NOSOUND;
                if (!showConfirmation)
                {
                    flags |= SHERB_NOCONFIRMATION;
                }

                uint result = SHEmptyRecycleBin(IntPtr.Zero, null, flags);

                if (result == 0)
                    Console.WriteLine("✅ Корзина успешно очищена!");
                else
                    Console.WriteLine($"❌ Ошибка очистки корзины. Код: {result}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка: {ex.Message}");
            }
        }
        static WaveInEvent waveIn;
        static VoskRecognizer rec;
        static bool isListening = true;
        static bool isAwake = false;
        static System.Timers.Timer sleepTimer;
        // 👇 добавь это поле в класс Program (вверху, рядом с другими статическими переменными)
        static bool waitingForSearchQuery = false;

        static async Task Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // 🎤 Настройка синтезатора речи
            var synth = new SpeechSynthesizer();
            synth.Volume = 100;
            synth.Rate = 2;
            synth.SelectVoiceByHints(VoiceGender.Male, VoiceAge.Adult, 0, new System.Globalization.CultureInfo("ru-RU"));

            // 🤖 Модель Vosk
            Vosk.Vosk.SetLogLevel(-1);
            var model = new Model(@"D:\Project C#\МойВаня\model");
            rec = new VoskRecognizer(model, 16000.0f);

            waveIn = new WaveInEvent
            {
                DeviceNumber = 0,
                WaveFormat = new WaveFormat(16000, 1)
            };

            sleepTimer = new System.Timers.Timer(15000);
            sleepTimer.Elapsed += (s, e) =>
            {
                isAwake = false;
                Console.WriteLine("💤 Джарвис уснул. Жду команду активации...");
            };
            static void PauseMicrophone()
            {
                try
                {
                    waveIn.StopRecording();
                    Console.WriteLine("🎧 Микрофон временно отключён (Джарвис думает)");
                }
                catch { }
            }

            static void ResumeMicrophone()
            {
                try
                {
                    waveIn.StartRecording();
                    Console.WriteLine("🎙 Микрофон снова активен");
                }
                catch { }
            }

            waveIn.DataAvailable += async (s, e) =>
            {
                if (!isListening) return;

                if (rec.AcceptWaveform(e.Buffer, e.BytesRecorded))
                {
                    var json = rec.Result();
                    var text = JsonDocument.Parse(json).RootElement.GetProperty("text").GetString()?.ToLower();

                    if (string.IsNullOrWhiteSpace(text)) return;
                    Console.WriteLine($"Ты сказал: {text}");

                    // 🚨 Активация по слову "джарвис"
                    if (IsSimilar(text, "джарвис"))
                    {
                        isAwake = true;
                        sleepTimer.Stop();
                        await PlayVoiceAsync(@"D:\music\Да сэр.wav");
                        StartListening();
                        sleepTimer.Start();
                        return;
                    }
                    if (text.Contains("поиск"))
                    {
                        await PlayVoiceAsync(@"D:\music\чтобу.mp3");
                        waitingForSearchQuery = true; // Включаем режим ожидания запроса  
                        return; // Выходим, чтобы не пошло в обычный блок
                    }

                    // 👇 Добавляем проверку — если сейчас ждём поисковый запрос:
                    if (waitingForSearchQuery)
                    {
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            waitingForSearchQuery = false; // Сбрасываем режим поиска
                            StopListening(); // Не слушаем, пока отвечает

                            await PlayVoiceAsync(@"D:\music\Ищу.mp3");

                            var psi = new ProcessStartInfo
                            {
                                FileName = @"C:\Users\Ivan\AppData\Local\Programs\Ollama\ollama.exe",
                                Arguments = $"run llama3:8b \"Отвечай кратко и по существу,на русском, как Джарвис. Вопрос: {text}\"",
                                RedirectStandardOutput = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };

                            var process = new Process { StartInfo = psi };
                            process.Start();

                            string response = await process.StandardOutput.ReadToEndAsync();
                            process.WaitForExit();

                            Console.WriteLine($"\n🔍 Ответ: {response}");

                            if (response.Length > 200)
                                response = response.Substring(0, 200) + "...";

                            await SpeakAsync(synth, response);
                            StartListening();

                        }
                        return; // Завершаем цикл — чтобы не обрабатывало как обычную команду
                    }

                    if (!isAwake) return; // Спит, если не активирован

                    sleepTimer.Stop(); // Сброс таймера, если пользователь говорит
                    await ProcessCommand(text, synth);
                    sleepTimer.Start(); // Снова слушает 15 секунд после выполнения
                }
            };

            waveIn.StartRecording();
            Console.WriteLine("🎙 Джарвис активен. Скажи 'Джарвис', чтобы его разбудить.");

            var tcs = new TaskCompletionSource<bool>();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                tcs.SetResult(true);
            };

            await tcs.Task;
            waveIn.StopRecording();
            waveIn.Dispose();
        }

        // ⚙️ Обработка команд
        static async Task ProcessCommand(string text, SpeechSynthesizer synth)
        {
            StopListening();

            if (IsSimilar(text, "привет") || IsSimilar(text, "я пришел"))
            {

                Process.Start(@"C:\Users\Ivan\AppData\Local\Programs\YandexMusic\Яндекс Музыка.exe");
                Process.Start("cmd", "/c start devenv");
                await PlayVoiceAsync(@"D:\music\Привет.mp3");
                await PlayVoiceAsync(@"D:\music\Хорошего.mp3");
            }
            else if (text.Contains("как дела") || text.Contains("как жизнь"))
            {
                await PlayVoiceAsync(@"D:\music\уменя.mp3");
                //  KillProcess("Code");
            }
            else if (text.Contains("очистить корзину") || text.Contains("очисти корзину"))
            {

                await PlayVoiceAsync(@"D:\music\[jhcth.mp3");
                EmptyRecycleBin();
            }
            else if (text.Contains("спасибо") || text.Contains("классно") || text.Contains("смешно") || text.Contains("молодец"))
            {
                await PlayVoiceAsync(@"D:\music\Всегда к вашим услугам сэр.wav");//Всегда к вашим услугам сэр

            }
            else if (text.Contains("закрой") && text.Contains("скотт"))
            {
                await PlayVoiceAsync(@"D:\music\Закрыв.mp3");
                KillProcess("devenv");
            }
            else if (text.Contains("открой") && text.Contains("скотт"))
            {
                await PlayVoiceAsync(@"D:\music\Откры.mp3");
                Process.Start("cmd", "/c start devenv");
            }
            else if (text.Contains("закрой") && text.Contains("юнити"))
            {
                await SpeakAsync(synth, "Закрываю Юнити.");
                KillProcess("Unity Hub");
            }
            else if (text.Contains("юнити"))
            {
                await SpeakAsync(synth, "Запускаю Юнити.");
                Process.Start("cmd", "/c start UnityHub");
            }
            else if (text.Contains("включи") && text.Contains("музык") || text.Contains("музыка") || text.Contains("музыку"))
            {


                Process[] ym = Process.GetProcessesByName("Яндекс Музыка");
                if (ym.Length == 0)
                {
                    Process.Start(@"C:\Users\Ivan\AppData\Local\Programs\YandexMusic\Яндекс Музыка.exe");
                    await PlayVoiceAsync(@"D:\music\Включаю.mp3");


                }
                else
                {
                    var yandexProc = ym[0];
                    SetForegroundWindow(yandexProc.MainWindowHandle);
                    await PlayVoiceAsync(@"D:\music\Включаю.mp3");
                }

            }
            else if (IsSimilar(text, "погода"))
            {
                string[] weathers = { "солнечно", "пасмурно", "дождь", "снег", "облачно" };
                var random = new Random();
                await SpeakAsync(synth, $"Сейчас {weathers[random.Next(weathers.Length)]}, около пятнадцати градусов.");
            }
            else if (text.Contains("напомни через"))
            {
                int minutes = ExtractNumber(text);
                string task = text.Split("через")[1].Replace($"{minutes}", "").Trim();
                _ = Task.Run(async () =>
                {
                    await Task.Delay(minutes * 60000);
                 //   await PlayVoiceAsync(@"D:\music\Сэр_пора.mp3");
                    await SpeakAsync(synth, $"Напоминаю: {task}");
                });
                await PlayVoiceAsync(@"D:\music\ХорошоСэр.mp3");
            }
            else if (text.Contains("заметка") || text.Contains("напомни"))
            {
                await PlayVoiceAsync(@"D:\music\Что запи.mp3");
                StopListening();

                string noteText = await ListenForResponseAsync(10); // слушает 10 секунд

                if (string.IsNullOrWhiteSpace(noteText))
                {
                    await PlayVoiceAsync(@"D:\music\Хорошо.mp3");
                }
                else
                {
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    string notePath = Path.Combine(desktop, $"Заметка_{DateTime.Now:HH-mm-ss}.txt");
                    File.WriteAllText(notePath, noteText);
                    await PlayVoiceAsync(@"D:\music\Заметка.mp3");
                }

                StartListening();
            }
           

            else if (text.Contains("открой") && text.Contains("браузер"))
            {



                Process.Start("cmd", "/c start chrome");
                await PlayVoiceAsync(@"D:\music\Открываю.mp3");
            }
            else if (text.Contains("число") || text.Contains("дата"))
            {
                await SpeakAsync(synth, $"Сегодня {DateTime.Now:dd MMMM yyyy}");
            }
            else if (text.Contains("игровой режим") || text.Contains("играть"))
            {

                Process.Start("cmd", "/c start steamwebhelper");
                //await SpeakAsync(synth, $"Сегодня {DateTime.Now:dd MMMM yyyy}");
            }
            else if (text.Contains("закрой") && text.Contains("сеть") || text.Contains("закрой") && text.Contains("америка"))
            {
                KillProcess("AdGuardVpn");
            }
            else if (text.Contains("америка") || text.Contains("сеть"))
            {
                // steamwebhelper            и  AdGuardVpn
                Process.Start("cmd", "/c start AdGuardVpn");
                await PlayVoiceAsync(@"D:\music\Запускаю.mp3");
            }
            
            else if (text.Contains("шутка") || text.Contains("шутку") || text.Contains("скучно"))
            {
                await Shutka();

            }
            else if (text.Contains("список команд"))
            {
                await PlayVoiceAsync(@"D:\music\Вот.mp3");
                Console.WriteLine(@"
🧭 Команды Джарвиса:
- 'привет'
- 'открой скот' / 'закрой скот'
- 'открой юнити' / 'закрой юнити'
- 'включи музыку'
- 'какая погода'
- 'сделай заметку'
- 'открой браузер'
- 'какое сегодня число'
- 'список команд'
");
            }
            else if (text.Contains("пока"))
            {
                KillProcess("devenv");
                KillProcess("Code");
                KillProcess("Unity Hub");
                await PlayVoiceAsync(@"D:\music\До встре.mp3");
                await PlayVoiceAsync(@"D:\music\Хорошего.mp3");
                isAwake = false;


            }
            /* else if (text.Contains("скриншот"))
             {
                 var bmp = new Bitmap(Screen.PrimaryScreen.Bounds.Width, Screen.PrimaryScreen.Bounds.Height);
                 using (var g = Graphics.FromImage(bmp))
                 {
                     g.CopyFromScreen(0, 0, 0, 0, bmp.Size);
                 }
                 string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"Скриншот_{DateTime.Now:HH-mm-ss}.png");
                 bmp.Save(path);
                 await PlayVoiceAsync(@"D:\music\Готово.mp3");
             }*/
            else if (text.Contains("выключи компьютер"))
            {
                await PlayVoiceAsync(@"D:\music\ХорошоСэр.mp3");
                Process.Start("shutdown", "/s /t 5");
            }
            else if (text.Contains("перезагрузи компьютер"))
            {
                await PlayVoiceAsync(@"D:\music\ХорошоСэр.mp3");
                Process.Start("shutdown", "/r /t 5");
            }
            else if (text.Contains("заблокируй экран"))
            {
                await PlayVoiceAsync(@"D:\music\ХорошоСэр.mp3");
                Process.Start("rundll32.exe", "user32.dll,LockWorkStation");
            }
            else if (text.Contains("процессы") || text.Contains("процесса") || text.Contains("процессе") || text.Contains("процессу"))//процессу
            {
                var b = Process.GetProcessesByName("");
                var chrom = 1;
                var vsCode = 1;
                foreach (var proc in b)
                {
                    var i = proc.ProcessName;// ждёт 5 секунд
                    if (i == "chrome" && chrom <= 1)
                    {
                        await SpeakAsync(synth, $"хром");
                        Console.WriteLine(i);
                        chrom++;
                    }
                    if (i == "devenv" && vsCode <= 1)
                    {
                        Console.WriteLine(i);
                        await SpeakAsync(synth, $"висуал студио код");
                        vsCode++;
                    }
                    // Console.WriteLine(i);
                    /*else
                    {
                         await SpeakAsync(synth, $"{i}");
                    }*/
                }
            }
            else if (text.Contains("заткнись") || text.Contains("спи") || text.Contains("замолчи") || text.Contains("вырубись") || text.Contains("вырубить")) //вырубить
            {
                isAwake = false;


            }
            else if (text.Contains("проводник"))
            {
                Process.Start("cmd", "/c start explorer");   // explorer
            }
            else if (text.Contains("включи  ютуб") || text.Contains("открой ютуб") || text.Contains("ютуб") || text.Contains("включи  ютюб") || text.Contains("открой ютюб") || text.Contains("ютюб"))
            {
                await PlayVoiceAsync(@"D:\music\ХорошоСэр.mp3");

                if (text.Contains("доза футбола") || text.Contains("дозу футбола") || text.Contains("дозу футболу"))
                {
                    Process.Start("cmd", "/c start https://www.youtube.com/@dozafutbola");//ОткрЮттуб         ХорошоСэр       ДозаФутбола

                    await PlayVoiceAsync(@"D:\music\ОткрЮттуб.mp3");

                    await PlayVoiceAsync(@"D:\music\ДозаФутбола.mp3");

                }
                else
                {
                    Process.Start("cmd", "/c start https://www.youtube.com/");
                    await PlayVoiceAsync(@"D:\music\ОткрЮттуб.mp3");
                }





            }
            else
            {


                await PlayVoiceAsync(@"D:\music\шо ты ск.mp3");
            }
            if(IsFuck)
            {
              isAwake = false;
            }
            else
            {
                StartListening();
            }
                
        }
public static int ExtractNumber(string input)
        {
            var words = input.Split(' ');
            foreach (var word in words)
            {
                if (int.TryParse(word, out int number))
                {
                    return number;
                }
            }
            return 0; // Возвращаем 0, если число не найдено
        }
        // 🎤 Синхронная речь с блокировкой слушания
        static async Task SpeakAsync(SpeechSynthesizer synth, string text)
        {
            StopListening();
            await Task.Run(() => synth.Speak(text));
            StartListening();
        }

        // 💀 Завершение процессов
        static void KillProcess(string name)
        {
            var processes = Process.GetProcessesByName(name);
            foreach (var p in processes)
            {
                try
                {
                    p.Kill();
                    p.WaitForExit();
                }
                catch { }
            }
        }

        // 🧮 Алгоритм расстояния Левенштейна
        static bool IsSimilar(string input, string target, int threshold = 2)
        {
            int distance = LevenshteinDistance(input, target);
            return distance <= threshold || input.Contains(target);
        }

        static int LevenshteinDistance(string s, string t)
        {
            int[,] d = new int[s.Length + 1, t.Length + 1];

            for (int i = 0; i <= s.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= t.Length; j++) d[0, j] = j;

            for (int i = 1; i <= s.Length; i++)
                for (int j = 1; j <= t.Length; j++)
                {
                    int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(
                        d[i - 1, j] + 1,
                        d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }

            return d[s.Length, t.Length];
        }

        // 🎧 Управление слушанием
        static void StopListening()
        {
            isListening = false;
            Console.WriteLine("🔇 Слушание приостановлено");
        }

        static void StartListening()
        {
            isListening = true;
            Console.WriteLine("🎙 Слушание возобновлено");
        }


        // 🎧 Слушает ответ пользователя (10 секунд) и возвращает распознанный текст
        static async Task<string> ListenForResponseAsync(int seconds)
        {
            string resultText = "";
            var model = new Model(@"D:\Project C#\МойВаня\model");
            var recTemp = new VoskRecognizer(model, 16000.0f);
            var waveTemp = new WaveInEvent
            {
                DeviceNumber = 0,
                WaveFormat = new WaveFormat(16000, 1)
            };

            var tcs = new TaskCompletionSource<string>();

            waveTemp.DataAvailable += (s, e) =>
            {
                if (recTemp.AcceptWaveform(e.Buffer, e.BytesRecorded))
                {
                    var json = recTemp.Result();
                    var text = JsonDocument.Parse(json).RootElement.GetProperty("text").GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        resultText = text;
                        tcs.TrySetResult(resultText);
                    }
                }
            };

            waveTemp.StartRecording();
            Console.WriteLine("📝 Слушаю для заметки...");

            await Task.Delay(seconds * 1000);
            waveTemp.StopRecording();
            waveTemp.Dispose();

            if (string.IsNullOrWhiteSpace(resultText))
            {
                Console.WriteLine("⏱ Ничего не сказано.");
                return "";
            }

            Console.WriteLine($"📋 Текст заметки: {resultText}");
            return resultText;
        }

        // Помести этот метод в тот же класс, где у тебя есть SpeakAsync
        public static async Task Shutka()
        {
            // Массив шуток
            string[] shut =[

       @"D:\music\Если_тебе_тяжело,_значит,_ты_идёшь.mp3",//Если_тебе_тяжело,_значит,_ты_идёшь
      @"D:\music\Оптимист_видит_стакан_наполовину_полным.mp3",//Оптимист_видит_стакан_наполовину_полным
       @"D:\music\Раньше_я_думал,_что_кофе_помогает_проснуться.mp3",//Раньше_я_думал,_что_кофе_помогает_проснуться
        @"D:\music\Ничто_так_не_украшает_человека,_как_выключенный_свет.mp3",//Ничто_так_не_украшает_человека,_как_выключенный_свет
       @"D:\music\Сначала_я_хотел_быть_идеальным,_потом_понял.mp3",//Сначала_я_хотел_быть_идеальным,_потом_понял
       @"D:\music\Моя_диета_проста_вижу_еду_—_ем_еду.mp3",//Моя_диета_проста_вижу_еду_—_ем_еду
        @"D:\music\Деньги_не_главное,_но_когда_их_нет.mp3",//Деньги_не_главное,_но_когда_их_нет
        @"D:\music\Если_долго_смотреть_в_холодильник.mp3",//Если_долго_смотреть_в_холодильник
       @"D:\music\Не все герои носят плащи.mp3",//Не все герои носят плащи
        @"D:\music\Мои_жизненные_планы_такие_же_чёткие.mp3",//Мои_жизненные_планы_такие_же_чёткие
        @"D:\music\Говорят, утро добрым не бывает.mp3",//Говорят, утро добрым не бывает
       @"D:\music\Кто_рано_встаёт,_тому_потом_весь_день.mp3",//Кто_рано_встаёт,_тому_потом_весь_день
        @"D:\music\Ничто так не ускоряет движение.mp3",//
@"D:\music\Я_не_ленивый,_я_просто_берегу_энергию.mp3",//Я_не_ленивый,_я_просто_берегу_энергию
       @"D:\music\Мой_организм_думает,_что_я_в_отпуске.mp3",//Мой_организм_думает,_что_я_в_отпуске
        @"D:\music\Если тебе скучно.mp3",//Если тебе скучно
      @"D:\music\Я_не_толстый,_просто_мой_характер_не.mp3",///Я_не_толстый,_просто_мой_характер_не
       @"D:\music\Хочу_похудеть,_но_мой_холодильник.mp3",//Хочу_похудеть,_но_мой_холодильник
        @"D:\music\Иногда_мне_кажется,_что_мой_диван_меня_любит.mp3",//Иногда_мне_кажется,_что_мой_диван_меня_любит
       @"D:\music\Не_могу_понять,_зачем_вставать_утром.mp3",//Не_могу_понять,_зачем_вставать_утром
@"D:\music\Сон_—_лучший_ответ_на_все_вопросы.mp3",//Сон_—_лучший_ответ_на_все_вопросы
       @"D:\music\Я не забываю.mp3",//Я не забываю.mp3
       @"D:\music\Не важно, что ты ешь.mp3",//Не важно, что ты ешь
       @"D:\music\Мой фитнес-трекер думает.mp3",//Мой фитнес-трекер думает
        @"D:\music\Когда_я_говорил,_что_займусь_спортом.mp3",//Когда_я_говорил,_что_займусь_спортом
        @"D:\music\Самая_короткая_мотивационная_речь.mp3",//Самая_короткая_мотивационная_речь
@"D:\music\Люблю людей на расстоянии.mp3",//Люблю людей на расстоянии
@"D:\music\Если жизнь тебе улыбнулась.mp3",//Если жизнь тебе улыбнулась.mp3
@"D:\music\Моё_хобби_—_планировать_великие_дела.mp3",//Моё_хобби_—_планировать_великие_дела
       @"D:\music\Никогда не поздно начать.mp3",///Никогда не поздно начать
@"D:\music\Если долго ничего не делать.mp3",//Если долго ничего не делать
@"D:\music\Хорошее_настроение_—_это_временно_неисправное.mp3",//      Хорошее_настроение_—_это_временно_неисправное     
        @"D:\music\Работа_—_это_место,_где_я_мечтаю_об_отпуске.mp3",//Работа_—_это_место,_где_я_мечтаю_об_отпуске
@"D:\music\Не спеши делать выводы.mp3",//Не спеши делать выводы
@"D:\music\Я_не_ворчу_—_я_просто_громко_размышляю.mp3",//Я_не_ворчу_—_я_просто_громко_размышляю
@"D:\music\Мечты сбываются.mp3",//Мечты сбываются
@"D:\music\Когда_мне_скучно,_я_начинаю_убираться.mp3",//Когда_мне_скучно,_я_начинаю_убираться
@"D:\music\Если нельзя, но очень хочется.mp3",//Если нельзя, но очень хочется
       @"D:\music\Лучше поздно, чем никогда.mp3",//Лучше поздно, чем никогда
        @"D:\music\Хочу на море.mp3",//Хочу на море
       @"D:\music\Счастье_—_это_когда_ничего_не_болит.mp3",//Счастье_—_это_когда_ничего_не_болит
       @"D:\music\Любая диета заканчивается там.mp3",///Любая диета заканчивается там
      @"D:\music\Я_не_спорю,_я_объясняю,_почему_я_прав.mp3",//Я_не_спорю,_я_объясняю,_почему_я_прав
        @"D:\music\Терпение_—_это_когда_стоишь_в_очереди.mp3",//Терпение_—_это_когда_стоишь_в_очереди
      @"D:\music\Если бы лень была болезнью.mp3",//Если бы лень была болезнью
       // "Терпение — это когда стоишь в очереди и улыбаешься тем, кто без очереди.",//
        @"D:\music\Если бы лень была болезнью.mp3",//Если бы лень была болезнью
        @"D:\music\Главное_в_жизни_—_не_потерять_пульт_от_телевизора.mp3",////Главное_в_жизни_—_не_потерять_пульт_от_телевизора
       @"D:\music\Я не ем после шести.mp3",//Я не ем после шести
        @"D:\music\Хорошее_настроение_—_штука_редкая.mp3",//Хорошее_настроение_—_штука_редкая.mp3
       @"D:\music\Если_ты_не_можешь_найти_смысл_жизни.mp3"//Если_ты_не_можешь_найти_смысл_жизни.mp3
    ];
            //////   @"D:\music\шо ты ск.mp3"   \\\\\\
            // Выбираем случайную шутку
            var rnd = new Random();
            string joke = $"{shut[rnd.Next(shut.Length)]}";
            
            // Пишем в консоль
            Console.WriteLine(joke);
            await PlayVoiceAsync(joke);
            // Озвучиваем (используем переданный synth)
            //  await SpeakAsync(synth, joke);
        }

    }
}
