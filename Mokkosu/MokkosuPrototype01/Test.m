﻿rec f = \x -> if x == 0 then 1 else x * f (x - 1)
in print (f 5)