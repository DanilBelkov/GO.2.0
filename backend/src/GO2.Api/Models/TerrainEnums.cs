namespace GO2.Api.Models;

// Класс условного знака (основные и дополнительные классы ориентирования).
public enum TerrainClass
{
    Vegetation = 0,
    Hydrography = 1,
    RocksAndStones = 2,
    Relief = 3,
    ManMade = 4,
    CourseMarkings = 5,
    SkiTrackMarkings = 6,
    TechnicalSymbols = 7
}

public static class TerrainClassExtensions
{
    public static string GetRussianName(this TerrainClass terrainClass) =>
        terrainClass switch
        {
            TerrainClass.Vegetation => "Растительность",
            TerrainClass.Hydrography => "Гидрография",
            TerrainClass.RocksAndStones => "Скалы и камни",
            TerrainClass.Relief => "Рельеф",
            TerrainClass.ManMade => "Искусственные объекты",
            TerrainClass.CourseMarkings => "Обозначения дистанции",
            TerrainClass.SkiTrackMarkings => "Обозначения лыжней",
            TerrainClass.TechnicalSymbols => "Технические символы",
            _ => "Неизвестный класс"
        };
}

// Поддерживаемые геометрии в редакторе MVP.
public enum TerrainGeometryKind
{
    Point = 0,
    Line = 1,
    Polygon = 2
}

// Источник появления объекта: автопайплайн или ручное редактирование.
public enum TerrainObjectSource
{
    Auto = 0,
    Manual = 1
}

// Состояния жизненного цикла фоновой оцифровки.
public enum DigitizationJobStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    Failed = 3
}
