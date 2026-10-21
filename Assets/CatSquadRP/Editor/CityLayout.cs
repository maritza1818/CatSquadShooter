namespace CatSquad.RescateProfundo.EditorTools
{
    /// <summary>
    /// GATÓPOLIS, la ciudad inundada (versión en primera persona). Corte lateral de la avenida principal:
    /// fila 0 = arriba, cada celda = 2 m, el nivel del mar está a -5 m (entre las filas 2 y 3).
    /// Los edificios de las veredas, a los lados de la avenida (Z), los genera el constructor.
    ///
    ///   #  suelo (calle / lecho marino)      X  dique de concreto        B  edificio dentro de la avenida
    ///   =  puente o paso elevado             H  compuerta de esclusa     |  -  reja (el brazo pasa, el submarino no)
    ///   .  agua                              S  muelle / base            P  bomba de DRENAJE del barrio
    ///   M  caja de chatarra   F  bidón   R  escombro   K  jaula con gatito   $  cascabel secreto
    ///   x  pez   w  algas   o  roca        a..g  puntos del tutorial
    ///
    /// De izquierda a derecha: mar abierto (nivel 0) | esclusa 1 | barrio 1 | esclusa 2 | barrio 2 | esclusa 3 | barrio 3.
    /// Todos los barrios empiezan inundados hasta el nivel del mar; cada bomba drena el suyo.
    /// </summary>
    public static class CityLayout
    {
        public const float CellSize = 2f;
        public const float SeaLevel = -5f;

        public struct Zone
        {
            public string id;
            public string name;
            public float order;       // 0 = mar, 1..3 barrios, x.5 = esclusas
            public int col0, col1;
            public bool isLock;
            public float drainedLevel; // nivel al completar su bomba (barrios)
            public int raftCol;        // columna de la balsa de rescate (-1 = sin balsa)
        }

        public static readonly Zone[] Zones =
        {
            new Zone { id = "mar",      name = "Mar abierto",          order = 0f,   col0 = 0,  col1 = 18, drainedLevel = -5f,  raftCol = -1 },
            new Zone { id = "esclusa1", name = "Esclusa 1",            order = 0.5f, col0 = 19, col1 = 23, isLock = true, raftCol = -1 },
            new Zone { id = "barrio1",  name = "Barrio del Puerto",    order = 1f,   col0 = 24, col1 = 49, drainedLevel = -17f, raftCol = 29 },
            new Zone { id = "esclusa2", name = "Esclusa 2",            order = 1.5f, col0 = 50, col1 = 54, isLock = true, raftCol = -1 },
            new Zone { id = "barrio2",  name = "Plaza del Ovillo",     order = 2f,   col0 = 55, col1 = 77, drainedLevel = -33f, raftCol = 62 },
            new Zone { id = "esclusa3", name = "Esclusa 3",            order = 2.5f, col0 = 78, col1 = 82, isLock = true, raftCol = -1 },
            new Zone { id = "barrio3",  name = "Torres del Maullido",  order = 3f,   col0 = 83, col1 = 99, drainedLevel = -55f, raftCol = 96 },
        };

        /// <summary>Bombas de drenaje en orden de X: la i-ésima drena este barrio.</summary>
        public static readonly string[] PumpDrains = { "barrio1", "barrio2", "barrio3" };

        public static readonly string[] Rows =
        {
            "....................................................................................................",
            "...................X...X..........................X...X.......................X...X.................",
            "...................X...X..........................X...X.......................X...X.................",
            "......S............H...H..........................H...H.......................H...H................#",
            "...................H...H............x.............H...H.......................H...H................#",
            "..............x....H...H............$.............H...H.......................H...H................#",
            "...x...............H.e.H..........................H...H.......................H...H................#",
            ".$......a..........H...H........M.................H...H.......................H...H................#",
            ".BBBB..........d...H...H...g..======..............H...H...............x.......H...H................#",
            ".BBBB.......c......H...H................MK........H...H...............$.......H...H................#",
            ".BBM|.b............H...H.....x..........BBB.......H...H...P...................H...H...P............#",
            "#BBBBo.wMxw....oPw.H...H................BBB.......H...H..BBBB.................H...H..BBBB..........#",
            "############RR#####XXXXX................BBB.x.....H...H..BBBB.................H...H..BBBB..........#",
            "############FF#####XXXXX..........................H...H..BBBB.....M...........H...H..BBBB..........#",
            "###################XXXXX..........................H...H..BBBB...======........H...H..BBBB..x$......#",
            "###################XXXXX.w..M....K...Mw........KwMH...H..BBBB.................H...H..BBBB..........#",
            "###################XXXXX##-#################RR####X...H.......................H...H..BBBB..........#",
            "###################XXXXX##M#################FF####X...H.......................H...H..BBBB..........#",
            "###################XXXXX##########################X...H.......x...............H...H..BBBB..........#",
            "###################XXXXX##########################X...H.......................H...H..BBBB..........#",
            "###################XXXXX##########################XXXXX....................x..H...H..BBBB.x........#",
            "###################XXXXX##########################XXXXX.................M.....H...H..BBBB..........#",
            "###################XXXXX##########################XXXXX................BBB....H...H..BBBB..........#",
            "###################XXXXX##########################XXXXX................BBB....H...H..BBBB..........#",
            "###################XXXXX##########################XXXXX................BBB....H...H..BBBB..........#",
            "###################XXXXX##########################XXXXX.w....MK....M.w.BBB.KM.H...H..BBBB..........#",
            "###################XXXXX##########################XXXXX#########RR############X...H..BBBB..........#",
            "###################XXXXX##########################XXXXX#########FF############X...H................#",
            "###################XXXXX##########################XXXXX#######################X...H.............x..#",
            "###################XXXXX##########################XXXXX#######################X...H.........M......#",
            "###################XXXXX##########################XXXXX#######################X...H.......=====....#",
            "###################XXXXX##########################XXXXX#######################XXXXX................#",
            "###################XXXXX##########################XXXXX#######################XXXXX................#",
            "###################XXXXX##########################XXXXX#######################XXXXX................#",
            "###################XXXXX##########################XXXXX#######################XXXXX................#",
            "###################XXXXX##########################XXXXX#######################XXXXX...............$#",
            "###################XXXXX##########################XXXXX#######################XXXXXwM....KMF...MwK.#",
            "###################XXXXX##########################XXXXX#######################XXXXX###########-#####",
            "###################XXXXX##########################XXXXX#######################XXXXX###########M#####",
            "###################XXXXX##########################XXXXX#######################XXXXX#################",
            "###################XXXXX##########################XXXXX#######################XXXXX#################",
            "###################XXXXX##########################XXXXX#######################XXXXX#################"
        };
    }
}
