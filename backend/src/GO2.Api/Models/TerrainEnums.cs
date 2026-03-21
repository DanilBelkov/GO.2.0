namespace GO2.Api.Models;

// Семантический класс местности для оцифровки и дальнейшей маршрутизации.
public enum TerrainClass
{
    Vegetation = 0,
    Water = 1,
    Rock = 2,
    Ground = 3,
    ManMade = 4
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
