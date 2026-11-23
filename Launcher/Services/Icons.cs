using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace BattleLauncher;

// Значки интерфейса. Геометрия — три набора (круглый, угловатый, пиксельный) на холсте 24×24; какой набор и как отрисовать
// (тонкая линия, жирная, залитая плашкой, двухцветная, неон, неоморфная плашка, пиксели) — решает скин. Всё рисуется кодом, файлов нет.
// Набор «угловатый» получен из круглого автоматически (кривые превращены в грани), «пиксельный» — растеризацией на сетку 16×16.
static class Icons
{
    static readonly Dictionary<string, string> SetR = new()
    {
        ["apps"] = "M4,4 H10.5 V10.5 H4 Z M13.5,4 H20 V10.5 H13.5 Z M4,13.5 H10.5 V20 H4 Z M13.5,13.5 H20 V20 H13.5 Z",
        ["style"] = "M12,3 A9,9 0 1 0 12,21 C13.6,21 14.2,19.7 13.3,18.7 C12.4,17.7 13,16.4 14.6,16.4 H17 A4,4 0 0 0 21,12.4 C21,7 17,3 12,3 Z M7.5,11 A1,1 0 1 0 7.5,11.01 M11,7.5 A1,1 0 1 0 11,7.51 M15.5,8 A1,1 0 1 0 15.5,8.01",
        ["net"] = "M12,3 L19.5,6 V12 C19.5,16.5 16.5,19.8 12,21 C7.5,19.8 4.5,16.5 4.5,12 V6 Z M8.5,12 L11,14.5 L15.5,9.5",
        ["ext"] = "M4,6 H10 V8 A2,2 0 1 1 14,8 V6 H20 V11 H18 A2,2 0 1 0 18,15 H20 V20 H4 Z",
        ["env"] = "M13,2.5 L5,13.5 H11 L10,21.5 L19,10 H13 Z",
        ["phone"] = "M8,2.5 H16 A1.5,1.5 0 0 1 17.5,4 V20 A1.5,1.5 0 0 1 16,21.5 H8 A1.5,1.5 0 0 1 6.5,20 V4 A1.5,1.5 0 0 1 8,2.5 Z M10.5,18.5 H13.5",
        ["skins"] = "M12,3 L14,10 L21,12 L14,14 L12,21 L10,14 L3,12 L10,10 Z",
        ["feedback"] = "M4,5 H20 V16 H11 L7,20 V16 H4 Z M8,9 H16 M8,12 H13",
        ["account"] = "M8.5,8 A3.5,3.5 0 1 0 15.5,8 A3.5,3.5 0 1 0 8.5,8 Z M4.5,20 C4.5,15.5 8,13.5 12,13.5 C16,13.5 19.5,15.5 19.5,20 Z",
        ["game"] = "M7,8 H17 A5,5 0 0 1 22,13 V15 A3,3 0 0 1 16,16 L15,14.5 H9 L8,16 A3,3 0 0 1 2,15 V13 A5,5 0 0 1 7,8 Z M7,11 V14 M5.5,12.5 H8.5 M16.5,11.5 H16.6 M18.5,13.5 H18.6",
        ["video"] = "M3,6.5 H14.5 V17.5 H3 Z M14.5,10.5 L21,7 V17 L14.5,13.5",
        ["admin"] = "M12,3 L19,6 V12 C19,16 16,19 12,21 C8,19 5,16 5,12 V6 Z M12,8 V13 M12,15.8 V15.9",
        ["update"] = "M12,4 V15 M7.5,10.5 L12,15 L16.5,10.5 M5,19.5 H19",
        ["play"] = "M8,5 L19,12 L8,19 Z",
        ["globe"] = "M3,12 A9,9 0 1 0 21,12 A9,9 0 1 0 3,12 Z M3,12 H21 M12,3 C8,7.5 8,16.5 12,21 M12,3 C16,7.5 16,16.5 12,21",
        ["ai"] = "M7,7 H17 V17 H7 Z M10,10 H14 V14 H10 Z M9,3 V7 M15,3 V7 M9,17 V21 M15,17 V21 M3,9 H7 M3,15 H7 M17,9 H21 M17,15 H21",
    };
    static readonly Dictionary<string, string> SetA = new()
    {
        ["apps"] = "M4,4 L10.5,4 L10.5,10.5 L4,10.5 Z M13.5,4 L20,4 L20,10.5 L13.5,10.5 Z M4,13.5 L10.5,13.5 L10.5,20 L4,20 Z M13.5,13.5 L20,13.5 L20,20 L13.5,20 Z",
        ["style"] = "M12,3 L5.64,5.64 L3,12 L5.64,18.36 L12,21 L13.68,19.99 L13.35,16.77 L19.22,15.73 L21,12.4 L18.38,5.67 Z M7.5,11 L5.51,10.9 L7.5,11.01 M11,7.5 L9.01,7.4 L11,7.51 M15.5,8 L13.51,7.9 L15.5,8.01",
        ["net"] = "M12,3 L19.5,6 L18.56,16.07 L12,21 L6.56,17.74 L4.5,12 L4.5,6 Z M8.5,12 L11,14.5 L15.5,9.5",
        ["ext"] = "M4,6 L10,6 L10,8 L11.61,6.04 L14,8 L14,6 L20,6 L20,11 L16,13 L20,15 L20,20 L4,20 Z",
        ["env"] = "M13,2.5 L5,13.5 L11,13.5 L10,21.5 L19,10 L13,10 Z",
        ["phone"] = "M8,2.5 L17.39,3.43 L16.57,21.39 L6.61,20.57 L6.5,4 Z M10.5,18.5 L13.5,18.5",
        ["skins"] = "M12,3 L14,10 L21,12 L14,14 L12,21 L10,14 L3,12 L10,10 Z",
        ["feedback"] = "M4,5 L20,5 L20,16 L11,16 L7,20 L7,16 L4,16 Z M8,9 L16,9 M8,12 L13,12",
        ["account"] = "M8.5,8 L12,11.5 L15.5,8 L12,4.5 Z M4.5,20 L6.75,15.06 L12,13.5 L17.25,15.06 L19.5,20 Z",
        ["game"] = "M7,8 L19.78,8.84 L21.9,12.02 L21.47,17.27 L18.31,18.46 L15,14.5 L9,14.5 L6.77,17.97 L3.39,18.08 L2.38,11.09 Z M7,11 L7,14 M5.5,12.5 L8.5,12.5 M16.5,11.5 L16.6,11.5 M18.5,13.5 L18.6,13.5",
        ["video"] = "M3,6.5 L14.5,6.5 L14.5,17.5 L3,17.5 Z M14.5,10.5 L21,7 L21,17 L14.5,13.5",
        ["admin"] = "M12,3 L19,6 L18.07,15.67 L12,21 L7,17.25 L5,12 L5,6 Z M12,8 L12,13 M12,15.8 L12,15.9",
        ["update"] = "M12,4 L12,15 M7.5,10.5 L12,15 L16.5,10.5 M5,19.5 L19,19.5",
        ["play"] = "M8,5 L19,12 L8,19 Z",
        ["globe"] = "M3,12 L5.64,18.36 L12,21 L18.36,18.36 L21,12 L18.36,5.64 L12,3 L5.64,5.64 Z M3,12 L21,12 M12,3 L9,12 L12,21 M12,3 L15,12 L12,21",
        ["ai"] = "M7,7 L17,7 L17,17 L7,17 Z M10,10 L14,10 L14,14 L10,14 Z M9,3 L9,7 M15,3 L15,7 M9,17 L9,21 M15,17 L15,21 M3,9 L7,9 M3,15 L7,15 M17,9 L21,9 M17,15 L21,15",
    };
    static readonly Dictionary<string, string> SetP = new()
    {
        ["apps"] = "M3,3 H21 V4.5 H3 Z M3,4.5 H21 V6 H3 Z M3,6 H4.5 V7.5 H3 Z M9,6 H15 V7.5 H9 Z M19.5,6 H21 V7.5 H19.5 Z M3,7.5 H4.5 V9 H3 Z M9,7.5 H15 V9 H9 Z M19.5,7.5 H21 V9 H19.5 Z M3,9 H21 V10.5 H3 Z M3,10.5 H21 V12 H3 Z M3,12 H12 V13.5 H3 Z M13.5,12 H21 V13.5 H13.5 Z M3,13.5 H21 V15 H3 Z M3,15 H4.5 V16.5 H3 Z M9,15 H15 V16.5 H9 Z M19.5,15 H21 V16.5 H19.5 Z M3,16.5 H4.5 V18 H3 Z M9,16.5 H15 V18 H9 Z M19.5,16.5 H21 V18 H19.5 Z M3,18 H6 V19.5 H3 Z M9,18 H15 V19.5 H9 Z M18,18 H21 V19.5 H18 Z M3,19.5 H21 V21 H3 Z",
        ["style"] = "M9,1.5 H15 V3 H9 Z M6,3 H18 V4.5 H6 Z M4.5,4.5 H7.5 V6 H4.5 Z M16.5,4.5 H19.5 V6 H16.5 Z M3,6 H6 V7.5 H3 Z M7.5,6 H12 V7.5 H7.5 Z M13.5,6 H16.5 V7.5 H13.5 Z M18,6 H21 V7.5 H18 Z M3,7.5 H4.5 V9 H3 Z M7.5,7.5 H16.5 V9 H7.5 Z M19.5,7.5 H21 V9 H19.5 Z M1.5,9 H7.5 V10.5 H1.5 Z M13.5,9 H15 V10.5 H13.5 Z M19.5,9 H22.5 V10.5 H19.5 Z M1.5,10.5 H9 V12 H1.5 Z M19.5,10.5 H22.5 V12 H19.5 Z M1.5,12 H4.5 V13.5 H1.5 Z M6,12 H7.5 V13.5 H6 Z M19.5,12 H22.5 V13.5 H19.5 Z M1.5,13.5 H4.5 V15 H1.5 Z M19.5,13.5 H22.5 V15 H19.5 Z M3,15 H4.5 V16.5 H3 Z M13.5,15 H21 V16.5 H13.5 Z M3,16.5 H6 V18 H3 Z M12,16.5 H18 V18 H12 Z M4.5,18 H7.5 V19.5 H4.5 Z M12,18 H15 V19.5 H12 Z M6,19.5 H15 V21 H6 Z M9,21 H13.5 V22.5 H9 Z",
        ["net"] = "M10.5,1.5 H13.5 V3 H10.5 Z M7.5,3 H16.5 V4.5 H7.5 Z M4.5,4.5 H10.5 V6 H4.5 Z M15,4.5 H21 V6 H15 Z M3,6 H6 V7.5 H3 Z M18,6 H21 V7.5 H18 Z M3,7.5 H6 V9 H3 Z M18,7.5 H21 V9 H18 Z M3,9 H6 V10.5 H3 Z M13.5,9 H16.5 V10.5 H13.5 Z M18,9 H21 V10.5 H18 Z M3,10.5 H6 V12 H3 Z M7.5,10.5 H9 V12 H7.5 Z M12,10.5 H15 V12 H12 Z M18,10.5 H21 V12 H18 Z M3,12 H6 V13.5 H3 Z M7.5,12 H13.5 V13.5 H7.5 Z M18,12 H21 V13.5 H18 Z M3,13.5 H6 V15 H3 Z M9,13.5 H13.5 V15 H9 Z M18,13.5 H21 V15 H18 Z M4.5,15 H6 V16.5 H4.5 Z M18,15 H19.5 V16.5 H18 Z M4.5,16.5 H7.5 V18 H4.5 Z M16.5,16.5 H19.5 V18 H16.5 Z M6,18 H9 V19.5 H6 Z M15,18 H18 V19.5 H15 Z M7.5,19.5 H16.5 V21 H7.5 Z M10.5,21 H13.5 V22.5 H10.5 Z",
        ["ext"] = "M3,4.5 H21 V6 H3 Z M3,6 H21 V7.5 H3 Z M3,7.5 H4.5 V9 H3 Z M9,7.5 H10.5 V9 H9 Z M13.5,7.5 H15 V9 H13.5 Z M19.5,7.5 H21 V9 H19.5 Z M3,9 H4.5 V10.5 H3 Z M18,9 H21 V10.5 H18 Z M3,10.5 H4.5 V12 H3 Z M15,10.5 H21 V12 H15 Z M3,12 H4.5 V13.5 H3 Z M15,12 H18 V13.5 H15 Z M3,13.5 H4.5 V15 H3 Z M15,13.5 H21 V15 H15 Z M3,15 H4.5 V16.5 H3 Z M16.5,15 H21 V16.5 H16.5 Z M3,16.5 H4.5 V18 H3 Z M19.5,16.5 H21 V18 H19.5 Z M3,18 H6 V19.5 H3 Z M18,18 H21 V19.5 H18 Z M3,19.5 H21 V21 H3 Z",
        ["env"] = "M12,1.5 H13.5 V3 H12 Z M10.5,3 H13.5 V4.5 H10.5 Z M9,4.5 H13.5 V6 H9 Z M9,6 H13.5 V7.5 H9 Z M7.5,7.5 H10.5 V9 H7.5 Z M12,7.5 H13.5 V9 H12 Z M6,9 H9 V10.5 H6 Z M12,9 H19.5 V10.5 H12 Z M6,10.5 H7.5 V12 H6 Z M16.5,10.5 H19.5 V12 H16.5 Z M4.5,12 H12 V13.5 H4.5 Z M15,12 H18 V13.5 H15 Z M4.5,13.5 H12 V15 H4.5 Z M15,13.5 H16.5 V15 H15 Z M9,15 H12 V16.5 H9 Z M13.5,15 H16.5 V16.5 H13.5 Z M9,16.5 H15 V18 H9 Z M9,18 H13.5 V19.5 H9 Z M9,19.5 H12 V21 H9 Z M9,21 H12 V22.5 H9 Z",
        ["phone"] = "M6,1.5 H18 V3 H6 Z M6,3 H18 V4.5 H6 Z M6,4.5 H7.5 V6 H6 Z M16.5,4.5 H19.5 V6 H16.5 Z M6,6 H7.5 V7.5 H6 Z M16.5,6 H19.5 V7.5 H16.5 Z M6,7.5 H7.5 V9 H6 Z M16.5,7.5 H19.5 V9 H16.5 Z M6,9 H7.5 V10.5 H6 Z M16.5,9 H19.5 V10.5 H16.5 Z M6,10.5 H7.5 V12 H6 Z M16.5,10.5 H19.5 V12 H16.5 Z M6,12 H7.5 V13.5 H6 Z M16.5,12 H19.5 V13.5 H16.5 Z M6,13.5 H7.5 V15 H6 Z M16.5,13.5 H19.5 V15 H16.5 Z M6,15 H7.5 V16.5 H6 Z M16.5,15 H19.5 V16.5 H16.5 Z M6,16.5 H7.5 V18 H6 Z M16.5,16.5 H19.5 V18 H16.5 Z M6,18 H7.5 V19.5 H6 Z M9,18 H15 V19.5 H9 Z M16.5,18 H19.5 V19.5 H16.5 Z M6,19.5 H7.5 V21 H6 Z M16.5,19.5 H18 V21 H16.5 Z M6,21 H18 V22.5 H6 Z",
        ["skins"] = "M12,1.5 H13.5 V3 H12 Z M10.5,3 H13.5 V4.5 H10.5 Z M10.5,4.5 H13.5 V6 H10.5 Z M9,6 H15 V7.5 H9 Z M9,7.5 H15 V9 H9 Z M7.5,9 H12 V10.5 H7.5 Z M13.5,9 H16.5 V10.5 H13.5 Z M3,10.5 H10.5 V12 H3 Z M13.5,10.5 H22.5 V12 H13.5 Z M1.5,12 H10.5 V13.5 H1.5 Z M15,12 H22.5 V13.5 H15 Z M7.5,13.5 H12 V15 H7.5 Z M13.5,13.5 H18 V15 H13.5 Z M9,15 H15 V16.5 H9 Z M9,16.5 H15 V18 H9 Z M10.5,18 H13.5 V19.5 H10.5 Z M10.5,19.5 H13.5 V21 H10.5 Z M10.5,21 H13.5 V22.5 H10.5 Z",
        ["feedback"] = "M3,4.5 H21 V6 H3 Z M3,6 H4.5 V7.5 H3 Z M19.5,6 H21 V7.5 H19.5 Z M3,7.5 H4.5 V9 H3 Z M7.5,7.5 H16.5 V9 H7.5 Z M19.5,7.5 H21 V9 H19.5 Z M3,9 H4.5 V10.5 H3 Z M7.5,9 H16.5 V10.5 H7.5 Z M19.5,9 H21 V10.5 H19.5 Z M3,10.5 H4.5 V12 H3 Z M7.5,10.5 H13.5 V12 H7.5 Z M19.5,10.5 H21 V12 H19.5 Z M3,12 H4.5 V13.5 H3 Z M7.5,12 H13.5 V13.5 H7.5 Z M19.5,12 H21 V13.5 H19.5 Z M3,13.5 H4.5 V15 H3 Z M19.5,13.5 H21 V15 H19.5 Z M3,15 H7.5 V16.5 H3 Z M10.5,15 H21 V16.5 H10.5 Z M6,16.5 H12 V18 H6 Z M6,18 H10.5 V19.5 H6 Z M6,19.5 H9 V21 H6 Z",
        ["account"] = "M10.5,3 H13.5 V4.5 H10.5 Z M9,4.5 H15 V6 H9 Z M7.5,6 H10.5 V7.5 H7.5 Z M13.5,6 H16.5 V7.5 H13.5 Z M7.5,7.5 H10.5 V9 H7.5 Z M13.5,7.5 H16.5 V9 H13.5 Z M7.5,9 H10.5 V10.5 H7.5 Z M13.5,9 H16.5 V10.5 H13.5 Z M9,10.5 H15 V12 H9 Z M9,12 H15 V13.5 H9 Z M6,13.5 H18 V15 H6 Z M4.5,15 H7.5 V16.5 H4.5 Z M16.5,15 H19.5 V16.5 H16.5 Z M4.5,16.5 H6 V18 H4.5 Z M18,16.5 H19.5 V18 H18 Z M3,18 H6 V19.5 H3 Z M18,18 H21 V19.5 H18 Z M3,19.5 H21 V21 H3 Z",
        ["game"] = "M3,7.5 H21 V9 H3 Z M1.5,9 H4.5 V10.5 H1.5 Z M19.5,9 H22.5 V10.5 H19.5 Z M1.5,10.5 H3 V12 H1.5 Z M6,10.5 H9 V12 H6 Z M15,10.5 H18 V12 H15 Z M21,10.5 H22.5 V12 H21 Z M1.5,12 H3 V13.5 H1.5 Z M4.5,12 H9 V13.5 H4.5 Z M18,12 H19.5 V13.5 H18 Z M21,12 H24 V13.5 H21 Z M1.5,13.5 H3 V15 H1.5 Z M6,13.5 H16.5 V15 H6 Z M18,13.5 H19.5 V15 H18 Z M21,13.5 H24 V15 H21 Z M1.5,15 H3 V16.5 H1.5 Z M7.5,15 H10.5 V16.5 H7.5 Z M13.5,15 H16.5 V16.5 H13.5 Z M21,15 H24 V16.5 H21 Z M1.5,16.5 H4.5 V18 H1.5 Z M6,16.5 H9 V18 H6 Z M15,16.5 H18 V18 H15 Z M19.5,16.5 H22.5 V18 H19.5 Z M3,18 H7.5 V19.5 H3 Z M16.5,18 H21 V19.5 H16.5 Z",
        ["video"] = "M1.5,6 H16.5 V7.5 H1.5 Z M19.5,6 H22.5 V7.5 H19.5 Z M1.5,7.5 H4.5 V9 H1.5 Z M13.5,7.5 H22.5 V9 H13.5 Z M1.5,9 H4.5 V10.5 H1.5 Z M13.5,9 H18 V10.5 H13.5 Z M19.5,9 H22.5 V10.5 H19.5 Z M1.5,10.5 H4.5 V12 H1.5 Z M13.5,10.5 H16.5 V12 H13.5 Z M19.5,10.5 H22.5 V12 H19.5 Z M1.5,12 H4.5 V13.5 H1.5 Z M13.5,12 H16.5 V13.5 H13.5 Z M19.5,12 H22.5 V13.5 H19.5 Z M1.5,13.5 H4.5 V15 H1.5 Z M13.5,13.5 H18 V15 H13.5 Z M19.5,13.5 H22.5 V15 H19.5 Z M1.5,15 H4.5 V16.5 H1.5 Z M13.5,15 H22.5 V16.5 H13.5 Z M1.5,16.5 H16.5 V18 H1.5 Z M19.5,16.5 H22.5 V18 H19.5 Z",
        ["admin"] = "M10.5,1.5 H13.5 V3 H10.5 Z M7.5,3 H16.5 V4.5 H7.5 Z M4.5,4.5 H10.5 V6 H4.5 Z M13.5,4.5 H19.5 V6 H13.5 Z M4.5,6 H6 V7.5 H4.5 Z M18,6 H21 V7.5 H18 Z M4.5,7.5 H6 V9 H4.5 Z M10.5,7.5 H13.5 V9 H10.5 Z M18,7.5 H21 V9 H18 Z M4.5,9 H6 V10.5 H4.5 Z M10.5,9 H13.5 V10.5 H10.5 Z M18,9 H21 V10.5 H18 Z M4.5,10.5 H6 V12 H4.5 Z M10.5,10.5 H13.5 V12 H10.5 Z M18,10.5 H21 V12 H18 Z M4.5,12 H6 V13.5 H4.5 Z M10.5,12 H13.5 V13.5 H10.5 Z M18,12 H19.5 V13.5 H18 Z M4.5,13.5 H6 V15 H4.5 Z M18,13.5 H19.5 V15 H18 Z M4.5,15 H7.5 V16.5 H4.5 Z M10.5,15 H13.5 V16.5 H10.5 Z M16.5,15 H19.5 V16.5 H16.5 Z M6,16.5 H9 V18 H6 Z M15,16.5 H18 V18 H15 Z M7.5,18 H10.5 V19.5 H7.5 Z M13.5,18 H16.5 V19.5 H13.5 Z M9,19.5 H15 V21 H9 Z M10.5,21 H13.5 V22.5 H10.5 Z",
        ["update"] = "M10.5,3 H13.5 V4.5 H10.5 Z M10.5,4.5 H13.5 V6 H10.5 Z M10.5,6 H13.5 V7.5 H10.5 Z M10.5,7.5 H13.5 V9 H10.5 Z M7.5,9 H9 V10.5 H7.5 Z M10.5,9 H13.5 V10.5 H10.5 Z M16.5,9 H18 V10.5 H16.5 Z M6,10.5 H13.5 V12 H6 Z M15,10.5 H18 V12 H15 Z M7.5,12 H16.5 V13.5 H7.5 Z M9,13.5 H15 V15 H9 Z M10.5,15 H13.5 V16.5 H10.5 Z M4.5,18 H19.5 V19.5 H4.5 Z M4.5,19.5 H19.5 V21 H4.5 Z",
        ["play"] = "M7.5,4.5 H10.5 V6 H7.5 Z M7.5,6 H12 V7.5 H7.5 Z M7.5,7.5 H9 V9 H7.5 Z M10.5,7.5 H15 V9 H10.5 Z M7.5,9 H9 V10.5 H7.5 Z M13.5,9 H18 V10.5 H13.5 Z M7.5,10.5 H9 V12 H7.5 Z M15,10.5 H19.5 V12 H15 Z M7.5,12 H9 V13.5 H7.5 Z M16.5,12 H19.5 V13.5 H16.5 Z M7.5,13.5 H9 V15 H7.5 Z M13.5,13.5 H18 V15 H13.5 Z M7.5,15 H9 V16.5 H7.5 Z M10.5,15 H15 V16.5 H10.5 Z M7.5,16.5 H13.5 V18 H7.5 Z M7.5,18 H10.5 V19.5 H7.5 Z",
        ["globe"] = "M9,1.5 H15 V3 H9 Z M6,3 H18 V4.5 H6 Z M4.5,4.5 H7.5 V6 H4.5 Z M9,4.5 H15 V6 H9 Z M16.5,4.5 H19.5 V6 H16.5 Z M3,6 H6 V7.5 H3 Z M9,6 H10.5 V7.5 H9 Z M13.5,6 H15 V7.5 H13.5 Z M18,6 H21 V7.5 H18 Z M3,7.5 H4.5 V9 H3 Z M7.5,7.5 H10.5 V9 H7.5 Z M13.5,7.5 H16.5 V9 H13.5 Z M19.5,7.5 H21 V9 H19.5 Z M1.5,9 H4.5 V10.5 H1.5 Z M7.5,9 H10.5 V10.5 H7.5 Z M13.5,9 H16.5 V10.5 H13.5 Z M19.5,9 H22.5 V10.5 H19.5 Z M1.5,10.5 H22.5 V12 H1.5 Z M1.5,12 H22.5 V13.5 H1.5 Z M1.5,13.5 H4.5 V15 H1.5 Z M7.5,13.5 H10.5 V15 H7.5 Z M13.5,13.5 H16.5 V15 H13.5 Z M19.5,13.5 H22.5 V15 H19.5 Z M3,15 H4.5 V16.5 H3 Z M7.5,15 H10.5 V16.5 H7.5 Z M13.5,15 H16.5 V16.5 H13.5 Z M19.5,15 H21 V16.5 H19.5 Z M3,16.5 H6 V18 H3 Z M9,16.5 H10.5 V18 H9 Z M13.5,16.5 H15 V18 H13.5 Z M18,16.5 H21 V18 H18 Z M4.5,18 H7.5 V19.5 H4.5 Z M9,18 H15 V19.5 H9 Z M16.5,18 H19.5 V19.5 H16.5 Z M6,19.5 H18 V21 H6 Z M9,21 H15 V22.5 H9 Z",
        ["ai"] = "M9,1.5 H10.5 V3 H9 Z M15,1.5 H16.5 V3 H15 Z M7.5,3 H10.5 V4.5 H7.5 Z M13.5,3 H16.5 V4.5 H13.5 Z M7.5,4.5 H10.5 V6 H7.5 Z M13.5,4.5 H16.5 V6 H13.5 Z M6,6 H18 V7.5 H6 Z M3,7.5 H22.5 V9 H3 Z M1.5,9 H7.5 V10.5 H1.5 Z M9,9 H15 V10.5 H9 Z M16.5,9 H22.5 V10.5 H16.5 Z M6,10.5 H7.5 V12 H6 Z M9,10.5 H15 V12 H9 Z M16.5,10.5 H18 V12 H16.5 Z M6,12 H7.5 V13.5 H6 Z M9,12 H15 V13.5 H9 Z M16.5,12 H18 V13.5 H16.5 Z M3,13.5 H7.5 V15 H3 Z M9,13.5 H15 V15 H9 Z M16.5,13.5 H22.5 V15 H16.5 Z M1.5,15 H9 V16.5 H1.5 Z M15,15 H22.5 V16.5 H15 Z M6,16.5 H18 V18 H6 Z M7.5,18 H10.5 V19.5 H7.5 Z M13.5,18 H16.5 V19.5 H13.5 Z M7.5,19.5 H10.5 V21 H7.5 Z M13.5,19.5 H16.5 V21 H13.5 Z M7.5,21 H10.5 V22.5 H7.5 Z M13.5,21 H16.5 V22.5 H13.5 Z",
    };
    static readonly Dictionary<string, (string Set, string Style)> OfSkin = new()
    {
        ["ghost"] = ("R", "neon"),
        ["cp2"] = ("A", "neu"),
        ["neotokyo"] = ("A", "neon"),
        ["matrix"] = ("P", "pixel"),
        ["glitch"] = ("A", "bold"),
        ["holo"] = ("R", "neon"),
        ["circuit"] = ("A", "neon"),
        ["mission"] = ("A", "bold"),
        ["neumo"] = ("R", "neu"),
        ["neoa"] = ("A", "neu"),
        ["neob"] = ("R", "neu"),
        ["neoc"] = ("R", "solid"),
        ["neod"] = ("R", "duo"),
        ["neoe"] = ("R", "neu"),
        ["neof"] = ("A", "neu"),
        ["swiss"] = ("R", "thin"),
        ["material"] = ("R", "solid"),
        ["glass"] = ("R", "duo"),
        ["paper"] = ("R", "thin"),
        ["brutal"] = ("A", "bold"),
        ["win95"] = ("P", "pixel"),
        ["hifi"] = ("R", "plate"),
        ["kawaii"] = ("R", "solid"),
        ["sunset"] = ("R", "duo"),
        ["eink"] = ("A", "bold"),
        ["bento2"] = ("R", "solid"),
        ["ide"] = ("R", "thin"),
        ["forest"] = ("R", "duo"),
    };

    static readonly Dictionary<string, Geometry> cache = new();

    static Geometry Geo(string set, string key)
    {
        var id = set + ":" + key;
        if (cache.TryGetValue(id, out var g)) return g;
        var src = (set == "A" ? SetA : set == "P" ? SetP : SetR);
        if (!src.TryGetValue(key, out var d)) d = SetR["apps"];
        var geo = Geometry.Parse(d);
        geo.Freeze();
        cache[id] = geo;
        return geo;
    }

    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    static Brush B(string hex) { var b = new SolidColorBrush(C(hex)); b.Freeze(); return b; }

    public static (string Set, string Style) StyleOf(Skin s) => OfSkin.TryGetValue(s.Id, out var v) ? v : ("R", "thin");

    // key — имя значка; size — размер глифа; skin — чьим оформлением рисовать (по умолчанию текущий);
    // follow — глиф берёт цвет у кнопки, в которой лежит (пункты меню); plate — рисовать ли плашку у стилей, где она есть.
    public static FrameworkElement Make(string key, double size = 18, Skin? skin = null, bool follow = false, bool plate = true)
    {
        var s = skin ?? Skins.Current;
        var (set, style) = StyleOf(s);
        var path = new Path
        {
            Data = Geo(set, key), Width = 24, Height = 24, Stretch = Stretch.None,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeThickness = 1.3,
            SnapsToDevicePixels = false,
        };
        double glyph = size;
        Brush? plateBg = null; Brush? plateEdge = null; Effect? plateEffect = null; CornerRadius plateR = new(0); bool round = false;
        switch (style)
        {
            case "thin":
                path.StrokeThickness = 1.3;
                if (follow) path.SetBinding(Shape.StrokeProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1) });
                else path.Stroke = B(s.Muted);
                break;
            case "bold":
                path.StrokeThickness = 2.4; path.StrokeLineJoin = PenLineJoin.Miter;
                path.StrokeStartLineCap = path.StrokeEndLineCap = set == "A" ? PenLineCap.Square : PenLineCap.Flat;
                if (follow) path.SetBinding(Shape.StrokeProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1) });
                else path.Stroke = B(s.Text);
                break;
            case "duo":
                var f = C(s.Accent2); path.Fill = new SolidColorBrush(Color.FromArgb(0x55, f.R, f.G, f.B));
                path.Stroke = B(s.Accent); path.StrokeThickness = 1.8;
                break;
            case "neon":
                path.Stroke = B(s.Accent); path.StrokeThickness = 1.8; path.StrokeLineJoin = PenLineJoin.Miter;
                if (set == "A") path.StrokeStartLineCap = path.StrokeEndLineCap = PenLineCap.Square;
                path.Effect = new DropShadowEffect { Color = C(s.Accent), BlurRadius = 7, ShadowDepth = 0, Opacity = 0.85 };
                break;
            case "pixel":
                path.Fill = B(s.Accent); path.Stroke = null; path.StrokeThickness = 0;
                break;
            case "solid":
                path.Stroke = B(s.OnAccent); path.StrokeThickness = 1.8;
                if (plate) { plateBg = B(s.Accent); plateR = new CornerRadius(Math.Max(6, Math.Min(s.CardR, 17)) * size / 22.0); }
                else { path.Stroke = B(s.Accent); }
                break;
            case "neu":
                path.Stroke = B(s.Accent); path.StrokeThickness = 1.8;
                if (plate)
                {
                    plateBg = B(s.Panel); plateR = new CornerRadius(Math.Min(s.CardR, size * 0.75));
                    plateEffect = new DropShadowEffect { Color = s.Shadow == "neuD" ? Colors.Black : C("#A3B1C6"), BlurRadius = size * 0.45, ShadowDepth = size * 0.18, Direction = 315, Opacity = s.Shadow == "neuD" ? 0.75 : 0.8 };
                }
                break;
            case "plate":
                path.Stroke = B(s.Accent); path.StrokeThickness = 1.8;
                if (plate) { plateBg = new LinearGradientBrush(C(s.Panel2), C(s.Panel), 45); plateEdge = B(s.Line); round = true; }
                break;
            default:
                path.Stroke = B(s.Muted);
                break;
        }
        var box = new Viewbox { Width = glyph, Height = glyph, Child = path, Stretch = Stretch.Uniform };
        if (plateBg == null) return box;
        double p = size * 1.55;
        return new Border
        {
            Width = p, Height = p, Background = plateBg, BorderBrush = plateEdge, BorderThickness = new Thickness(plateEdge != null ? 1 : 0),
            CornerRadius = round ? new CornerRadius(p / 2) : plateR, Effect = plateEffect, Child = box,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
    }
}
