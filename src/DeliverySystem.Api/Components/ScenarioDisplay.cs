namespace DeliverySystem.Api.Components;

public static class ScenarioDisplay
{
    private static readonly (string Name, string Title, string Description)[] Items =
    [
        ("add-today-before-cutoff", "Сегодня, до 10:00", "Заказ на сегодня, пришёл до 10:00 — попадёт в доставку на сегодня."),
        ("add-today-after-cutoff", "Сегодня, после 10:00", "Пришёл после 10:00 — переносится на следующий рабочий день."),
        ("add-tomorrow", "На завтра", "Обычный заказ на следующую дату."),
        ("add-past-date", "Прошедшая дата", "Дата в прошлом — заказ помечается как некорректный (InvalidDate)."),
        ("cancel-lead", "Отмена лида", "Отмена в amoCRM — заказ переходит в статус «Отменён»."),
        ("duplicate-webhook", "Повторный вебхук", "Тот же лид приходит дважды — второй заказ не создаётся."),
        ("pending-point", "Точка на модерации", "Точка ещё не подтверждена — заказ создаётся, но заблокирован."),
        ("unknown-point", "Неизвестная точка", "Точки нет в справочнике — заказ не создаётся, ошибка в журнале вебхуков."),
        ("add-missing-responsible", "Без ответственного", "В лиде нет ответственного — SalesManagerUserId остаётся пустым.")
    ];

    private static readonly Dictionary<string, (string Title, string Description)> Map =
        Items.ToDictionary(i => i.Name, i => (i.Title, i.Description), StringComparer.OrdinalIgnoreCase);

    public static string Label(string name) =>
        Map.TryGetValue(name, out var item) ? item.Title : name;

    public static string Description(string name) =>
        Map.TryGetValue(name, out var item) ? item.Description : "Сценарий мока amoCRM.";

    public static int SortKey(string name)
    {
        for (var i = 0; i < Items.Length; i++)
        {
            if (string.Equals(Items[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return Items.Length;
    }
}
