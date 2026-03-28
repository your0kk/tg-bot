using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Microsoft.Data.Sqlite;

namespace TGBotV11
{
    class Program
    {
        static TelegramBotClient bot;
        static string connectionString = "Data Source=fitness.db";

        static async Task Main(string[] args)
        {
            var token = Environment.GetEnvironmentVariable("BOT_TOKEN");

            bot = new TelegramBotClient(token);

            InitDatabase();

            bot.StartReceiving(HandleUpdate, HandleError);

            Console.WriteLine("Бот запущен...");
            await Task.Delay(-1);
        }

        // 📦 СОЗДАНИЕ БАЗЫ
        static void InitDatabase()
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            var command = connection.CreateCommand();

            command.CommandText =
            @"
            CREATE TABLE IF NOT EXISTS workouts (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                userId INTEGER,
                date TEXT,
                name TEXT,
                minutes INTEGER
            );
            ";

            command.ExecuteNonQuery();
        }

        // 📩 ОБРАБОТКА СООБЩЕНИЙ
        static async Task HandleUpdate(ITelegramBotClient botClient, Update update, CancellationToken ct)
        {
            if (update.Type != UpdateType.Message || update.Message?.Text == null)
                return;

            var chatId = update.Message.Chat.Id;
            var text = update.Message.Text;

            var keyboard = new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton[] { "🏋️ Добавить тренировку" },
                new KeyboardButton[] { "📊 Мои отчёты" }
            })
            {
                ResizeKeyboard = true
            };

            if (text == "/start")
            {
                await botClient.SendMessage(chatId, "Привет! Выбери действие:", replyMarkup: keyboard);
                return;
            }

            // ➕ ДОБАВИТЬ ТРЕНИРОВКУ
            if (text == "🏋️ Добавить тренировку")
            {
                await botClient.SendMessage(chatId, "Напиши: упражнение минуты\n\nПример:\nГрудь 60");
                return;
            }

            // 👉 СОХРАНЕНИЕ
            if (text.Contains(" "))
            {
                var parts = text.Split(' ');

                if (parts.Length == 2 && int.TryParse(parts[1], out int minutes))
                {
                    string name = parts[0];
                    string date = DateTime.Now.ToString("dd.MM");

                    using var connection = new SqliteConnection(connectionString);
                    connection.Open();

                    var command = connection.CreateCommand();
                    command.CommandText =
                    @"
                    INSERT INTO workouts (userId, date, name, minutes)
                    VALUES ($userId, $date, $name, $minutes);
                    ";

                    command.Parameters.AddWithValue("$userId", chatId);
                    command.Parameters.AddWithValue("$date", date);
                    command.Parameters.AddWithValue("$name", name);
                    command.Parameters.AddWithValue("$minutes", minutes);

                    command.ExecuteNonQuery();

                    await botClient.SendMessage(chatId, "Тренировка сохранена ✅", replyMarkup: keyboard);
                    return;
                }
            }

            // 📊 ОТЧЕТ
            if (text == "📊 Мои отчёты")
            {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();

                var command = connection.CreateCommand();
                command.CommandText =
                @"
                SELECT date, name, minutes
                FROM workouts
                WHERE userId = $userId;
                ";

                command.Parameters.AddWithValue("$userId", chatId);

                using var reader = command.ExecuteReader();

                string report = "Твои тренировки:\n\n";
                int i = 1;

                while (reader.Read())
                {
                    string date = reader.GetString(0);
                    string name = reader.GetString(1);
                    int minutes = reader.GetInt32(2);

                    report += $"{i}) {date} — {name} — {minutes} мин\n";
                    i++;
                }

                if (i == 1)
                    report = "У тебя пока нет тренировок";

                await botClient.SendMessage(chatId, report);
                return;
            }
        }

        static Task HandleError(ITelegramBotClient botClient, Exception exception, CancellationToken ct)
        {
            Console.WriteLine(exception.Message);
            return Task.CompletedTask;
        }
    }
}