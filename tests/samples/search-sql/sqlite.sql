## q=
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## q=episode
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE "f"."Name" LIKE @pattern ESCAPE '\' AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE "f"."Name" LIKE @pattern ESCAPE '\' AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## q=episode -10, photo
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE (("f"."Name" LIKE @pattern ESCAPE '\' AND "f"."Name" NOT LIKE @pattern3 ESCAPE '\') OR "f"."Name" LIKE @pattern5 ESCAPE '\') AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE (("f"."Name" LIKE @pattern ESCAPE '\' AND "f"."Name" NOT LIKE @pattern3 ESCAPE '\') OR "f"."Name" LIKE @pattern5 ESCAPE '\') AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## q=*media/ep
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE "f"."FullName" LIKE @pattern ESCAPE '\' AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE "f"."FullName" LIKE @pattern ESCAPE '\' AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## q=e:mkv s:>100kb
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE "f"."Size" > @bytes AND "f"."Extension" LIKE @pattern ESCAPE '\' AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE "f"."Size" > @bytes AND "f"."Extension" LIKE @pattern ESCAPE '\' AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## q=-e:txt -s:<1kb
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE "f"."Extension" NOT LIKE @pattern ESCAPE '\' AND "f"."Size" >= @bytes AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE "f"."Extension" NOT LIKE @pattern ESCAPE '\' AND "f"."Size" >= @bytes AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## q=w:>=1000 h:<2000 r:1920x1080
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE "f"."Width" > 0 AND "f"."Width" >= @pixels AND "f"."Height" > 0 AND "f"."Height" < @pixels1 AND "f"."Width" > 0 AND "f"."Width" = @pixels2 AND "f"."Height" > 0 AND "f"."Height" = @pixels3 AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE "f"."Width" > 0 AND "f"."Width" >= @pixels AND "f"."Height" > 0 AND "f"."Height" < @pixels1 AND "f"."Width" > 0 AND "f"."Width" = @pixels2 AND "f"."Height" > 0 AND "f"."Height" = @pixels3 AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## q=-w:1920 r:hr
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE ((("f"."Extension" LIKE @pattern ESCAPE '\' OR "f"."Extension" LIKE @pattern3 ESCAPE '\' OR "f"."Extension" LIKE @pattern5 ESCAPE '\' OR "f"."Extension" LIKE @pattern7 ESCAPE '\' OR "f"."Extension" LIKE @pattern9 ESCAPE '\' OR "f"."Extension" LIKE @pattern11 ESCAPE '\' OR "f"."Extension" LIKE @pattern13 ESCAPE '\' OR "f"."Extension" LIKE @pattern15 ESCAPE '\' OR "f"."Extension" LIKE @pattern17 ESCAPE '\') AND "f"."Width" >= @pixels AND "f"."Height" >= @pixels) OR (("f"."Extension" LIKE @pattern21 ESCAPE '\' OR "f"."Extension" LIKE @pattern23 ESCAPE '\' OR "f"."Extension" LIKE @pattern25 ESCAPE '\' OR "f"."Extension" LIKE @pattern27 ESCAPE '\' OR "f"."Extension" LIKE @pattern29 ESCAPE '\' OR "f"."Extension" LIKE @pattern31 ESCAPE '\' OR "f"."Extension" LIKE @pattern33 ESCAPE '\' OR "f"."Extension" LIKE @pattern35 ESCAPE '\' OR "f"."Extension" LIKE @pattern37 ESCAPE '\' OR "f"."Extension" LIKE @pattern39 ESCAPE '\' OR "f"."Extension" LIKE @pattern41 ESCAPE '\' OR "f"."Extension" LIKE @pattern43 ESCAPE '\') AND "f"."Width" >= @pixels44 AND "f"."Height" >= @pixels44)) AND (CASE
        WHEN "f"."Width" > 0 THEN 0
        ELSE 1
    END OR "f"."Width" <> @pixels46 OR "f"."Width" IS NULL) AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ((("f"."Extension" LIKE @pattern ESCAPE '\' OR "f"."Extension" LIKE @pattern3 ESCAPE '\' OR "f"."Extension" LIKE @pattern5 ESCAPE '\' OR "f"."Extension" LIKE @pattern7 ESCAPE '\' OR "f"."Extension" LIKE @pattern9 ESCAPE '\' OR "f"."Extension" LIKE @pattern11 ESCAPE '\' OR "f"."Extension" LIKE @pattern13 ESCAPE '\' OR "f"."Extension" LIKE @pattern15 ESCAPE '\' OR "f"."Extension" LIKE @pattern17 ESCAPE '\') AND "f"."Width" >= @pixels AND "f"."Height" >= @pixels) OR (("f"."Extension" LIKE @pattern21 ESCAPE '\' OR "f"."Extension" LIKE @pattern23 ESCAPE '\' OR "f"."Extension" LIKE @pattern25 ESCAPE '\' OR "f"."Extension" LIKE @pattern27 ESCAPE '\' OR "f"."Extension" LIKE @pattern29 ESCAPE '\' OR "f"."Extension" LIKE @pattern31 ESCAPE '\' OR "f"."Extension" LIKE @pattern33 ESCAPE '\' OR "f"."Extension" LIKE @pattern35 ESCAPE '\' OR "f"."Extension" LIKE @pattern37 ESCAPE '\' OR "f"."Extension" LIKE @pattern39 ESCAPE '\' OR "f"."Extension" LIKE @pattern41 ESCAPE '\' OR "f"."Extension" LIKE @pattern43 ESCAPE '\') AND "f"."Width" >= @pixels44 AND "f"."Height" >= @pixels44)) AND (CASE
        WHEN "f"."Width" > 0 THEN 0
        ELSE 1
    END OR "f"."Width" <> @pixels46 OR "f"."Width" IS NULL) AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## q=r:lr
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE ((("f"."Extension" LIKE @pattern ESCAPE '\' OR "f"."Extension" LIKE @pattern3 ESCAPE '\' OR "f"."Extension" LIKE @pattern5 ESCAPE '\' OR "f"."Extension" LIKE @pattern7 ESCAPE '\' OR "f"."Extension" LIKE @pattern9 ESCAPE '\' OR "f"."Extension" LIKE @pattern11 ESCAPE '\' OR "f"."Extension" LIKE @pattern13 ESCAPE '\' OR "f"."Extension" LIKE @pattern15 ESCAPE '\' OR "f"."Extension" LIKE @pattern17 ESCAPE '\') AND "f"."Width" > 0 AND "f"."Height" > 0 AND ("f"."Width" < @pixels OR "f"."Height" < @pixels)) OR (("f"."Extension" LIKE @pattern21 ESCAPE '\' OR "f"."Extension" LIKE @pattern23 ESCAPE '\' OR "f"."Extension" LIKE @pattern25 ESCAPE '\' OR "f"."Extension" LIKE @pattern27 ESCAPE '\' OR "f"."Extension" LIKE @pattern29 ESCAPE '\' OR "f"."Extension" LIKE @pattern31 ESCAPE '\' OR "f"."Extension" LIKE @pattern33 ESCAPE '\' OR "f"."Extension" LIKE @pattern35 ESCAPE '\' OR "f"."Extension" LIKE @pattern37 ESCAPE '\' OR "f"."Extension" LIKE @pattern39 ESCAPE '\' OR "f"."Extension" LIKE @pattern41 ESCAPE '\' OR "f"."Extension" LIKE @pattern43 ESCAPE '\') AND "f"."Width" > 0 AND "f"."Height" > 0 AND ("f"."Width" < @pixels44 OR "f"."Height" < @pixels44))) AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ((("f"."Extension" LIKE @pattern ESCAPE '\' OR "f"."Extension" LIKE @pattern3 ESCAPE '\' OR "f"."Extension" LIKE @pattern5 ESCAPE '\' OR "f"."Extension" LIKE @pattern7 ESCAPE '\' OR "f"."Extension" LIKE @pattern9 ESCAPE '\' OR "f"."Extension" LIKE @pattern11 ESCAPE '\' OR "f"."Extension" LIKE @pattern13 ESCAPE '\' OR "f"."Extension" LIKE @pattern15 ESCAPE '\' OR "f"."Extension" LIKE @pattern17 ESCAPE '\') AND "f"."Width" > 0 AND "f"."Height" > 0 AND ("f"."Width" < @pixels OR "f"."Height" < @pixels)) OR (("f"."Extension" LIKE @pattern21 ESCAPE '\' OR "f"."Extension" LIKE @pattern23 ESCAPE '\' OR "f"."Extension" LIKE @pattern25 ESCAPE '\' OR "f"."Extension" LIKE @pattern27 ESCAPE '\' OR "f"."Extension" LIKE @pattern29 ESCAPE '\' OR "f"."Extension" LIKE @pattern31 ESCAPE '\' OR "f"."Extension" LIKE @pattern33 ESCAPE '\' OR "f"."Extension" LIKE @pattern35 ESCAPE '\' OR "f"."Extension" LIKE @pattern37 ESCAPE '\' OR "f"."Extension" LIKE @pattern39 ESCAPE '\' OR "f"."Extension" LIKE @pattern41 ESCAPE '\' OR "f"."Extension" LIKE @pattern43 ESCAPE '\') AND "f"."Width" > 0 AND "f"."Height" > 0 AND ("f"."Width" < @pixels44 OR "f"."Height" < @pixels44))) AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## q=d:>10m -d:<1h
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE "f"."Duration" > 0.0 AND "f"."Duration" > @seconds AND (CASE
        WHEN "f"."Duration" > 0.0 THEN 0
        ELSE 1
    END OR CASE
        WHEN "f"."Duration" < @seconds1 THEN 0
        ELSE 1
    END) AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE "f"."Duration" > 0.0 AND "f"."Duration" > @seconds AND (CASE
        WHEN "f"."Duration" > 0.0 THEN 0
        ELSE 1
    END OR CASE
        WHEN "f"."Duration" < @seconds1 THEN 0
        ELSE 1
    END) AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## q=type:video,type:audio
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE ("f"."Extension" LIKE @pattern ESCAPE '\' OR "f"."Extension" LIKE @pattern3 ESCAPE '\' OR "f"."Extension" LIKE @pattern5 ESCAPE '\' OR "f"."Extension" LIKE @pattern7 ESCAPE '\' OR "f"."Extension" LIKE @pattern9 ESCAPE '\' OR "f"."Extension" LIKE @pattern11 ESCAPE '\' OR "f"."Extension" LIKE @pattern13 ESCAPE '\' OR "f"."Extension" LIKE @pattern15 ESCAPE '\' OR "f"."Extension" LIKE @pattern17 ESCAPE '\' OR "f"."Extension" LIKE @pattern19 ESCAPE '\' OR "f"."Extension" LIKE @pattern21 ESCAPE '\' OR "f"."Extension" LIKE @pattern23 ESCAPE '\' OR "f"."Extension" LIKE @pattern25 ESCAPE '\' OR "f"."Extension" LIKE @pattern27 ESCAPE '\' OR "f"."Extension" LIKE @pattern29 ESCAPE '\' OR "f"."Extension" LIKE @pattern31 ESCAPE '\' OR "f"."Extension" LIKE @pattern33 ESCAPE '\' OR "f"."Extension" LIKE @pattern35 ESCAPE '\' OR "f"."Extension" LIKE @pattern37 ESCAPE '\') AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."Extension" LIKE @pattern ESCAPE '\' OR "f"."Extension" LIKE @pattern3 ESCAPE '\' OR "f"."Extension" LIKE @pattern5 ESCAPE '\' OR "f"."Extension" LIKE @pattern7 ESCAPE '\' OR "f"."Extension" LIKE @pattern9 ESCAPE '\' OR "f"."Extension" LIKE @pattern11 ESCAPE '\' OR "f"."Extension" LIKE @pattern13 ESCAPE '\' OR "f"."Extension" LIKE @pattern15 ESCAPE '\' OR "f"."Extension" LIKE @pattern17 ESCAPE '\' OR "f"."Extension" LIKE @pattern19 ESCAPE '\' OR "f"."Extension" LIKE @pattern21 ESCAPE '\' OR "f"."Extension" LIKE @pattern23 ESCAPE '\' OR "f"."Extension" LIKE @pattern25 ESCAPE '\' OR "f"."Extension" LIKE @pattern27 ESCAPE '\' OR "f"."Extension" LIKE @pattern29 ESCAPE '\' OR "f"."Extension" LIKE @pattern31 ESCAPE '\' OR "f"."Extension" LIKE @pattern33 ESCAPE '\' OR "f"."Extension" LIKE @pattern35 ESCAPE '\' OR "f"."Extension" LIKE @pattern37 ESCAPE '\') AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## q=type:other
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE "f"."Extension" NOT LIKE @pattern ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern3 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern5 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern7 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern9 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern11 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern13 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern15 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern17 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern19 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern21 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern23 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern25 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern27 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern29 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern31 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern33 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern35 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern37 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern39 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern41 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern43 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern45 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern47 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern49 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern51 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern53 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern55 ESCAPE '\' AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE "f"."Extension" NOT LIKE @pattern ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern3 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern5 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern7 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern9 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern11 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern13 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern15 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern17 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern19 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern21 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern23 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern25 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern27 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern29 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern31 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern33 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern35 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern37 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern39 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern41 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern43 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern45 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern47 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern49 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern51 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern53 ESCAPE '\' AND "f"."Extension" NOT LIKE @pattern55 ESCAPE '\' AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## q=t:Cat
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "t"."TagId", "t"."LastSearchedAt", "t"."LastViewedAt"
FROM "TagActivity" AS "t"
WHERE "t"."TagId" = @ids1
;
SELECT "t"."Id"
FROM "TagDefinitions" AS "t"
WHERE "t"."Id" = @ids1
;
INSERT INTO "TagActivity" ("TagId", "LastSearchedAt", "LastViewedAt")
VALUES (@p0, @p1, @p2);
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @ids1) AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f1"
        WHERE "f"."Id" = "f1"."FileId" AND "f1"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f2"
;
SELECT "f3"."FullName", "f3"."Name", "f3"."Extension", "f3"."Size", "f3"."LastWriteTime", "f3"."Width", "f3"."Height", "f3"."Duration", "f3"."Pixels", "f3"."Id", "f2"."TagId", "f2"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @ids1) AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f1"
        WHERE "f"."Id" = "f1"."FileId" AND "f1"."TagId" = @hiddenIds1)
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f3"
LEFT JOIN "FileTags" AS "f2" ON "f3"."Id" = "f2"."FileId"
ORDER BY "f3"."FullName", "f3"."Id", "f2"."FileId"
;

## q=-t:Cat t:Vacation
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "t"."TagId", "t"."LastSearchedAt", "t"."LastViewedAt"
FROM "TagActivity" AS "t"
WHERE "t"."TagId" = @ids1
;
SELECT "t"."Id"
FROM "TagDefinitions" AS "t"
WHERE "t"."Id" = @ids1
;
INSERT INTO "TagActivity" ("TagId", "LastSearchedAt", "LastViewedAt")
VALUES (@p0, @p1, @p2);
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @ids2) AND EXISTS (
        SELECT 1
        FROM "FileTags" AS "f1"
        WHERE "f"."Id" = "f1"."FileId" AND "f1"."TagId" = @ids11) AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f2"
        WHERE "f"."Id" = "f2"."FileId" AND "f2"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f3"
;
SELECT "f4"."FullName", "f4"."Name", "f4"."Extension", "f4"."Size", "f4"."LastWriteTime", "f4"."Width", "f4"."Height", "f4"."Duration", "f4"."Pixels", "f4"."Id", "f3"."TagId", "f3"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @ids2) AND EXISTS (
        SELECT 1
        FROM "FileTags" AS "f1"
        WHERE "f"."Id" = "f1"."FileId" AND "f1"."TagId" = @ids11) AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f2"
        WHERE "f"."Id" = "f2"."FileId" AND "f2"."TagId" = @hiddenIds1)
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f4"
LEFT JOIN "FileTags" AS "f3" ON "f4"."Id" = "f3"."FileId"
ORDER BY "f4"."FullName", "f4"."Id", "f3"."FileId"
;

## q=special:untagged
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId") AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f1"
        WHERE "f"."Id" = "f1"."FileId" AND "f1"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f2"
;
SELECT "f3"."FullName", "f3"."Name", "f3"."Extension", "f3"."Size", "f3"."LastWriteTime", "f3"."Width", "f3"."Height", "f3"."Duration", "f3"."Pixels", "f3"."Id", "f2"."TagId", "f2"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId") AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f1"
        WHERE "f"."Id" = "f1"."FileId" AND "f1"."TagId" = @hiddenIds1)
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f3"
LEFT JOIN "FileTags" AS "f2" ON "f3"."Id" = "f2"."FileId"
ORDER BY "f3"."FullName", "f3"."Id", "f2"."FileId"
;

## q=t:Archived
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "t"."TagId", "t"."LastSearchedAt", "t"."LastViewedAt"
FROM "TagActivity" AS "t"
WHERE "t"."TagId" = @ids1
;
SELECT "t"."Id"
FROM "TagDefinitions" AS "t"
WHERE "t"."Id" = @ids1
;
INSERT INTO "TagActivity" ("TagId", "LastSearchedAt", "LastViewedAt")
VALUES (@p0, @p1, @p2);
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @ids1) AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @ids1) AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix)
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## in Media
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE "f"."Name" LIKE @pattern ESCAPE '\' AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE "f"."Name" LIKE @pattern ESCAPE '\' AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## folder rules
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE "f"."Name" LIKE @pattern ESCAPE '\' AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ((("f"."FullName" LIKE @selfPattern11 ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern13 ESCAPE '\') AND ("f"."FullName" = @self14 OR substr("f"."FullName", 0 + 1, @length15) = @prefix16)) OR (("f"."FullName" LIKE @selfPattern18 ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern20 ESCAPE '\') AND ("f"."FullName" = @self21 OR substr("f"."FullName", 0 + 1, @length22) = @prefix23) AND (("f"."FullName" NOT LIKE @selfPattern25 ESCAPE '\' AND "f"."FullName" NOT LIKE @nestedPattern27 ESCAPE '\') OR ("f"."FullName" <> @self28 AND substr("f"."FullName", 0 + 1, @length29) <> @prefix30))))
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE "f"."Name" LIKE @pattern ESCAPE '\' AND ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ((("f"."FullName" LIKE @selfPattern11 ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern13 ESCAPE '\') AND ("f"."FullName" = @self14 OR substr("f"."FullName", 0 + 1, @length15) = @prefix16)) OR (("f"."FullName" LIKE @selfPattern18 ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern20 ESCAPE '\') AND ("f"."FullName" = @self21 OR substr("f"."FullName", 0 + 1, @length22) = @prefix23) AND (("f"."FullName" NOT LIKE @selfPattern25 ESCAPE '\' AND "f"."FullName" NOT LIKE @nestedPattern27 ESCAPE '\') OR ("f"."FullName" <> @self28 AND substr("f"."FullName", 0 + 1, @length29) <> @prefix30))))
    ORDER BY "f"."Name", "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."Name", "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Name desc=False page=1
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    ORDER BY "f"."Name", "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."Name", "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Name desc=False page=2
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ("f"."Name" > @text OR ("f"."Name" = @text AND "f"."FullName" > @path))
    ORDER BY "f"."Name", "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."Name", "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Name desc=False page=3
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ("f"."Name" > @text OR ("f"."Name" = @text AND "f"."FullName" > @path))
    ORDER BY "f"."Name", "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."Name", "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Name desc=True page=1
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    ORDER BY "f"."Name" DESC, "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."Name" DESC, "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Name desc=True page=2
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ("f"."Name" < @text OR ("f"."Name" = @text AND "f"."FullName" > @path))
    ORDER BY "f"."Name" DESC, "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."Name" DESC, "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Name desc=True page=3
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ("f"."Name" < @text OR ("f"."Name" = @text AND "f"."FullName" > @path))
    ORDER BY "f"."Name" DESC, "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."Name" DESC, "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Modified desc=False page=1
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    ORDER BY "f"."LastWriteTime", "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."LastWriteTime", "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Modified desc=False page=2
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ("f"."LastWriteTime" > @time OR ("f"."LastWriteTime" = @time AND "f"."FullName" > @path))
    ORDER BY "f"."LastWriteTime", "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."LastWriteTime", "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Modified desc=False page=3
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ("f"."LastWriteTime" > @time OR ("f"."LastWriteTime" = @time AND "f"."FullName" > @path))
    ORDER BY "f"."LastWriteTime", "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."LastWriteTime", "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Modified desc=True page=1
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    ORDER BY "f"."LastWriteTime" DESC, "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."LastWriteTime" DESC, "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Modified desc=True page=2
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ("f"."LastWriteTime" < @time OR ("f"."LastWriteTime" = @time AND "f"."FullName" > @path))
    ORDER BY "f"."LastWriteTime" DESC, "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."LastWriteTime" DESC, "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Modified desc=True page=3
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ("f"."LastWriteTime" < @time OR ("f"."LastWriteTime" = @time AND "f"."FullName" > @path))
    ORDER BY "f"."LastWriteTime" DESC, "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."LastWriteTime" DESC, "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Size desc=False page=1
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    ORDER BY "f"."Size", "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."Size", "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Size desc=False page=2
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ("f"."Size" > @number OR ("f"."Size" = @number AND "f"."FullName" > @path))
    ORDER BY "f"."Size", "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."Size", "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Size desc=False page=3
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ("f"."Size" > @number OR ("f"."Size" = @number AND "f"."FullName" > @path))
    ORDER BY "f"."Size", "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."Size", "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Size desc=True page=1
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    ORDER BY "f"."Size" DESC, "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."Size" DESC, "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Size desc=True page=2
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ("f"."Size" < @number OR ("f"."Size" = @number AND "f"."FullName" > @path))
    ORDER BY "f"."Size" DESC, "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."Size" DESC, "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Size desc=True page=3
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ("f"."Size" < @number OR ("f"."Size" = @number AND "f"."FullName" > @path))
    ORDER BY "f"."Size" DESC, "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."Size" DESC, "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Type desc=False page=1
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    ORDER BY "f"."Extension", "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."Extension", "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Type desc=False page=2
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ("f"."Extension" > @text OR ("f"."Extension" = @text AND "f"."FullName" > @path))
    ORDER BY "f"."Extension", "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."Extension", "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Type desc=False page=3
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ("f"."Extension" > @text OR ("f"."Extension" = @text AND "f"."FullName" > @path))
    ORDER BY "f"."Extension", "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."Extension", "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Type desc=True page=1
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    ORDER BY "f"."Extension" DESC, "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."Extension" DESC, "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Type desc=True page=2
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ("f"."Extension" < @text OR ("f"."Extension" = @text AND "f"."FullName" > @path))
    ORDER BY "f"."Extension" DESC, "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."Extension" DESC, "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Type desc=True page=3
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ("f"."Extension" < @text OR ("f"."Extension" = @text AND "f"."FullName" > @path))
    ORDER BY "f"."Extension" DESC, "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."Extension" DESC, "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Path desc=False page=1
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Path desc=False page=2
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND "f"."FullName" > @path
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Path desc=False page=3
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND "f"."FullName" > @path
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Path desc=True page=1
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    ORDER BY "f"."FullName" DESC
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName" DESC, "f2"."Id", "f1"."FileId"
;

## sort=Path desc=True page=2
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND "f"."FullName" < @path
    ORDER BY "f"."FullName" DESC
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName" DESC, "f2"."Id", "f1"."FileId"
;

## sort=Path desc=True page=3
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND "f"."FullName" < @path
    ORDER BY "f"."FullName" DESC
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName" DESC, "f2"."Id", "f1"."FileId"
;

## sort=Duration desc=False page=1
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND "f"."Duration" > 0.0
    ORDER BY "f"."Duration", "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."Duration", "f2"."FullName", "f2"."Id", "f1"."FileId"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ("f"."Duration" IS NULL OR "f"."Duration" <= 0.0)
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Duration desc=False page=2
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND "f"."Duration" > 0.0 AND ("f"."Duration" > @seconds OR ("f"."Duration" = @seconds AND "f"."FullName" > @path))
    ORDER BY "f"."Duration", "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."Duration", "f2"."FullName", "f2"."Id", "f1"."FileId"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ("f"."Duration" IS NULL OR "f"."Duration" <= 0.0)
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Duration desc=False page=3
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ("f"."Duration" IS NULL OR "f"."Duration" <= 0.0) AND "f"."FullName" > @path
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Duration desc=True page=1
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND "f"."Duration" > 0.0
    ORDER BY "f"."Duration" DESC, "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."Duration" DESC, "f2"."FullName", "f2"."Id", "f1"."FileId"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ("f"."Duration" IS NULL OR "f"."Duration" <= 0.0)
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Duration desc=True page=2
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND "f"."Duration" > 0.0 AND ("f"."Duration" < @seconds OR ("f"."Duration" = @seconds AND "f"."FullName" > @path))
    ORDER BY "f"."Duration" DESC, "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."Duration" DESC, "f2"."FullName", "f2"."Id", "f1"."FileId"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ("f"."Duration" IS NULL OR "f"."Duration" <= 0.0)
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Duration desc=True page=3
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ("f"."Duration" IS NULL OR "f"."Duration" <= 0.0) AND "f"."FullName" > @path
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Resolution desc=False page=1
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND "f"."Pixels" > 0
    ORDER BY "f"."Pixels", "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."Pixels", "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Resolution desc=False page=2
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND "f"."Pixels" > 0 AND ("f"."Pixels" > @number OR ("f"."Pixels" = @number AND "f"."FullName" > @path))
    ORDER BY "f"."Pixels", "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."Pixels", "f2"."FullName", "f2"."Id", "f1"."FileId"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ("f"."Pixels" IS NULL OR "f"."Pixels" <= 0)
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Resolution desc=False page=3
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ("f"."Pixels" IS NULL OR "f"."Pixels" <= 0) AND "f"."FullName" > @path
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Resolution desc=True page=1
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1)
    LIMIT @p
) AS "f1"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND "f"."Pixels" > 0
    ORDER BY "f"."Pixels" DESC, "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."Pixels" DESC, "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Resolution desc=True page=2
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND "f"."Pixels" > 0 AND ("f"."Pixels" < @number OR ("f"."Pixels" = @number AND "f"."FullName" > @path))
    ORDER BY "f"."Pixels" DESC, "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."Pixels" DESC, "f2"."FullName", "f2"."Id", "f1"."FileId"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ("f"."Pixels" IS NULL OR "f"."Pixels" <= 0)
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName", "f2"."Id", "f1"."FileId"
;

## sort=Resolution desc=True page=3
SELECT "t"."Id", "t"."Name", "t"."Shorthand", "t"."Color", "t"."IsCategory", "t"."IsHidden", "t"."DisambiguationId"
FROM "TagDefinitions" AS "t"
;
SELECT "t"."TagId", "t"."Name"
FROM "TagAliases" AS "t"
;
SELECT "t"."ParentId", "t"."ChildId"
FROM "TagParents" AS "t"
;
SELECT "f2"."FullName", "f2"."Name", "f2"."Extension", "f2"."Size", "f2"."LastWriteTime", "f2"."Width", "f2"."Height", "f2"."Duration", "f2"."Pixels", "f2"."Id", "f1"."TagId", "f1"."FileId"
FROM (
    SELECT "f"."FullName", "f"."Name", "f"."Extension", "f"."Size", "f"."LastWriteTime", "f"."Width", "f"."Height", "f"."Duration", "f"."Pixels", "f"."Id"
    FROM "Files" AS "f"
    WHERE ("f"."FullName" LIKE @selfPattern ESCAPE '\' OR "f"."FullName" LIKE @nestedPattern ESCAPE '\') AND ("f"."FullName" = @self OR substr("f"."FullName", 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM "FileTags" AS "f0"
        WHERE "f"."Id" = "f0"."FileId" AND "f0"."TagId" = @hiddenIds1) AND ("f"."Pixels" IS NULL OR "f"."Pixels" <= 0) AND "f"."FullName" > @path
    ORDER BY "f"."FullName"
    LIMIT @p
) AS "f2"
LEFT JOIN "FileTags" AS "f1" ON "f2"."Id" = "f1"."FileId"
ORDER BY "f2"."FullName", "f2"."Id", "f1"."FileId"
;

