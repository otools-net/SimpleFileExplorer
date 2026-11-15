## q=
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
    ORDER BY [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## q=episode
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE [f].[Name] LIKE @pattern ESCAPE N'\' AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE [f].[Name] LIKE @pattern ESCAPE N'\' AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
    ORDER BY [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## q=episode -10, photo
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE (([f].[Name] LIKE @pattern ESCAPE N'\' AND [f].[Name] NOT LIKE @pattern3 ESCAPE N'\') OR [f].[Name] LIKE @pattern5 ESCAPE N'\') AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE (([f].[Name] LIKE @pattern ESCAPE N'\' AND [f].[Name] NOT LIKE @pattern3 ESCAPE N'\') OR [f].[Name] LIKE @pattern5 ESCAPE N'\') AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
    ORDER BY [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## q=*media/ep
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE [f].[FullName] LIKE @pattern ESCAPE N'\' AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE [f].[FullName] LIKE @pattern ESCAPE N'\' AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
    ORDER BY [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## q=e:mkv s:>100kb
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE [f].[Size] > @bytes AND [f].[Extension] LIKE @pattern ESCAPE N'\' AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE [f].[Size] > @bytes AND [f].[Extension] LIKE @pattern ESCAPE N'\' AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
    ORDER BY [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## q=-e:txt -s:<1kb
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE [f].[Extension] NOT LIKE @pattern ESCAPE N'\' AND [f].[Size] >= @bytes AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE [f].[Extension] NOT LIKE @pattern ESCAPE N'\' AND [f].[Size] >= @bytes AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
    ORDER BY [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## q=w:>=1000 h:<2000 r:1920x1080
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE [f].[Width] > 0 AND [f].[Width] >= @pixels AND [f].[Height] > 0 AND [f].[Height] < @pixels1 AND [f].[Width] > 0 AND [f].[Width] = @pixels2 AND [f].[Height] > 0 AND [f].[Height] = @pixels3 AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE [f].[Width] > 0 AND [f].[Width] >= @pixels AND [f].[Height] > 0 AND [f].[Height] < @pixels1 AND [f].[Width] > 0 AND [f].[Width] = @pixels2 AND [f].[Height] > 0 AND [f].[Height] = @pixels3 AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
    ORDER BY [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## q=-w:1920 r:hr
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE ((([f].[Extension] LIKE @pattern ESCAPE N'\' OR [f].[Extension] LIKE @pattern3 ESCAPE N'\' OR [f].[Extension] LIKE @pattern5 ESCAPE N'\' OR [f].[Extension] LIKE @pattern7 ESCAPE N'\' OR [f].[Extension] LIKE @pattern9 ESCAPE N'\' OR [f].[Extension] LIKE @pattern11 ESCAPE N'\' OR [f].[Extension] LIKE @pattern13 ESCAPE N'\' OR [f].[Extension] LIKE @pattern15 ESCAPE N'\' OR [f].[Extension] LIKE @pattern17 ESCAPE N'\') AND [f].[Width] >= @pixels AND [f].[Height] >= @pixels) OR (([f].[Extension] LIKE @pattern21 ESCAPE N'\' OR [f].[Extension] LIKE @pattern23 ESCAPE N'\' OR [f].[Extension] LIKE @pattern25 ESCAPE N'\' OR [f].[Extension] LIKE @pattern27 ESCAPE N'\' OR [f].[Extension] LIKE @pattern29 ESCAPE N'\' OR [f].[Extension] LIKE @pattern31 ESCAPE N'\' OR [f].[Extension] LIKE @pattern33 ESCAPE N'\' OR [f].[Extension] LIKE @pattern35 ESCAPE N'\' OR [f].[Extension] LIKE @pattern37 ESCAPE N'\' OR [f].[Extension] LIKE @pattern39 ESCAPE N'\' OR [f].[Extension] LIKE @pattern41 ESCAPE N'\' OR [f].[Extension] LIKE @pattern43 ESCAPE N'\') AND [f].[Width] >= @pixels44 AND [f].[Height] >= @pixels44)) AND (CASE
        WHEN [f].[Width] > 0 THEN CAST(0 AS bit)
        ELSE CAST(1 AS bit)
    END = CAST(1 AS bit) OR [f].[Width] <> @pixels46 OR [f].[Width] IS NULL) AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ((([f].[Extension] LIKE @pattern ESCAPE N'\' OR [f].[Extension] LIKE @pattern3 ESCAPE N'\' OR [f].[Extension] LIKE @pattern5 ESCAPE N'\' OR [f].[Extension] LIKE @pattern7 ESCAPE N'\' OR [f].[Extension] LIKE @pattern9 ESCAPE N'\' OR [f].[Extension] LIKE @pattern11 ESCAPE N'\' OR [f].[Extension] LIKE @pattern13 ESCAPE N'\' OR [f].[Extension] LIKE @pattern15 ESCAPE N'\' OR [f].[Extension] LIKE @pattern17 ESCAPE N'\') AND [f].[Width] >= @pixels AND [f].[Height] >= @pixels) OR (([f].[Extension] LIKE @pattern21 ESCAPE N'\' OR [f].[Extension] LIKE @pattern23 ESCAPE N'\' OR [f].[Extension] LIKE @pattern25 ESCAPE N'\' OR [f].[Extension] LIKE @pattern27 ESCAPE N'\' OR [f].[Extension] LIKE @pattern29 ESCAPE N'\' OR [f].[Extension] LIKE @pattern31 ESCAPE N'\' OR [f].[Extension] LIKE @pattern33 ESCAPE N'\' OR [f].[Extension] LIKE @pattern35 ESCAPE N'\' OR [f].[Extension] LIKE @pattern37 ESCAPE N'\' OR [f].[Extension] LIKE @pattern39 ESCAPE N'\' OR [f].[Extension] LIKE @pattern41 ESCAPE N'\' OR [f].[Extension] LIKE @pattern43 ESCAPE N'\') AND [f].[Width] >= @pixels44 AND [f].[Height] >= @pixels44)) AND (CASE
        WHEN [f].[Width] > 0 THEN CAST(0 AS bit)
        ELSE CAST(1 AS bit)
    END = CAST(1 AS bit) OR [f].[Width] <> @pixels46 OR [f].[Width] IS NULL) AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
    ORDER BY [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## q=r:lr
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE ((([f].[Extension] LIKE @pattern ESCAPE N'\' OR [f].[Extension] LIKE @pattern3 ESCAPE N'\' OR [f].[Extension] LIKE @pattern5 ESCAPE N'\' OR [f].[Extension] LIKE @pattern7 ESCAPE N'\' OR [f].[Extension] LIKE @pattern9 ESCAPE N'\' OR [f].[Extension] LIKE @pattern11 ESCAPE N'\' OR [f].[Extension] LIKE @pattern13 ESCAPE N'\' OR [f].[Extension] LIKE @pattern15 ESCAPE N'\' OR [f].[Extension] LIKE @pattern17 ESCAPE N'\') AND [f].[Width] > 0 AND [f].[Height] > 0 AND ([f].[Width] < @pixels OR [f].[Height] < @pixels)) OR (([f].[Extension] LIKE @pattern21 ESCAPE N'\' OR [f].[Extension] LIKE @pattern23 ESCAPE N'\' OR [f].[Extension] LIKE @pattern25 ESCAPE N'\' OR [f].[Extension] LIKE @pattern27 ESCAPE N'\' OR [f].[Extension] LIKE @pattern29 ESCAPE N'\' OR [f].[Extension] LIKE @pattern31 ESCAPE N'\' OR [f].[Extension] LIKE @pattern33 ESCAPE N'\' OR [f].[Extension] LIKE @pattern35 ESCAPE N'\' OR [f].[Extension] LIKE @pattern37 ESCAPE N'\' OR [f].[Extension] LIKE @pattern39 ESCAPE N'\' OR [f].[Extension] LIKE @pattern41 ESCAPE N'\' OR [f].[Extension] LIKE @pattern43 ESCAPE N'\') AND [f].[Width] > 0 AND [f].[Height] > 0 AND ([f].[Width] < @pixels44 OR [f].[Height] < @pixels44))) AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ((([f].[Extension] LIKE @pattern ESCAPE N'\' OR [f].[Extension] LIKE @pattern3 ESCAPE N'\' OR [f].[Extension] LIKE @pattern5 ESCAPE N'\' OR [f].[Extension] LIKE @pattern7 ESCAPE N'\' OR [f].[Extension] LIKE @pattern9 ESCAPE N'\' OR [f].[Extension] LIKE @pattern11 ESCAPE N'\' OR [f].[Extension] LIKE @pattern13 ESCAPE N'\' OR [f].[Extension] LIKE @pattern15 ESCAPE N'\' OR [f].[Extension] LIKE @pattern17 ESCAPE N'\') AND [f].[Width] > 0 AND [f].[Height] > 0 AND ([f].[Width] < @pixels OR [f].[Height] < @pixels)) OR (([f].[Extension] LIKE @pattern21 ESCAPE N'\' OR [f].[Extension] LIKE @pattern23 ESCAPE N'\' OR [f].[Extension] LIKE @pattern25 ESCAPE N'\' OR [f].[Extension] LIKE @pattern27 ESCAPE N'\' OR [f].[Extension] LIKE @pattern29 ESCAPE N'\' OR [f].[Extension] LIKE @pattern31 ESCAPE N'\' OR [f].[Extension] LIKE @pattern33 ESCAPE N'\' OR [f].[Extension] LIKE @pattern35 ESCAPE N'\' OR [f].[Extension] LIKE @pattern37 ESCAPE N'\' OR [f].[Extension] LIKE @pattern39 ESCAPE N'\' OR [f].[Extension] LIKE @pattern41 ESCAPE N'\' OR [f].[Extension] LIKE @pattern43 ESCAPE N'\') AND [f].[Width] > 0 AND [f].[Height] > 0 AND ([f].[Width] < @pixels44 OR [f].[Height] < @pixels44))) AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
    ORDER BY [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## q=d:>10m -d:<1h
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE [f].[Duration] > 0.0E0 AND [f].[Duration] > @seconds AND (CASE
        WHEN [f].[Duration] > 0.0E0 THEN CAST(0 AS bit)
        ELSE CAST(1 AS bit)
    END = CAST(1 AS bit) OR CASE
        WHEN [f].[Duration] < @seconds1 THEN CAST(0 AS bit)
        ELSE CAST(1 AS bit)
    END = CAST(1 AS bit)) AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE [f].[Duration] > 0.0E0 AND [f].[Duration] > @seconds AND (CASE
        WHEN [f].[Duration] > 0.0E0 THEN CAST(0 AS bit)
        ELSE CAST(1 AS bit)
    END = CAST(1 AS bit) OR CASE
        WHEN [f].[Duration] < @seconds1 THEN CAST(0 AS bit)
        ELSE CAST(1 AS bit)
    END = CAST(1 AS bit)) AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
    ORDER BY [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## q=type:video,type:audio
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[Extension] LIKE @pattern ESCAPE N'\' OR [f].[Extension] LIKE @pattern3 ESCAPE N'\' OR [f].[Extension] LIKE @pattern5 ESCAPE N'\' OR [f].[Extension] LIKE @pattern7 ESCAPE N'\' OR [f].[Extension] LIKE @pattern9 ESCAPE N'\' OR [f].[Extension] LIKE @pattern11 ESCAPE N'\' OR [f].[Extension] LIKE @pattern13 ESCAPE N'\' OR [f].[Extension] LIKE @pattern15 ESCAPE N'\' OR [f].[Extension] LIKE @pattern17 ESCAPE N'\' OR [f].[Extension] LIKE @pattern19 ESCAPE N'\' OR [f].[Extension] LIKE @pattern21 ESCAPE N'\' OR [f].[Extension] LIKE @pattern23 ESCAPE N'\' OR [f].[Extension] LIKE @pattern25 ESCAPE N'\' OR [f].[Extension] LIKE @pattern27 ESCAPE N'\' OR [f].[Extension] LIKE @pattern29 ESCAPE N'\' OR [f].[Extension] LIKE @pattern31 ESCAPE N'\' OR [f].[Extension] LIKE @pattern33 ESCAPE N'\' OR [f].[Extension] LIKE @pattern35 ESCAPE N'\' OR [f].[Extension] LIKE @pattern37 ESCAPE N'\') AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[Extension] LIKE @pattern ESCAPE N'\' OR [f].[Extension] LIKE @pattern3 ESCAPE N'\' OR [f].[Extension] LIKE @pattern5 ESCAPE N'\' OR [f].[Extension] LIKE @pattern7 ESCAPE N'\' OR [f].[Extension] LIKE @pattern9 ESCAPE N'\' OR [f].[Extension] LIKE @pattern11 ESCAPE N'\' OR [f].[Extension] LIKE @pattern13 ESCAPE N'\' OR [f].[Extension] LIKE @pattern15 ESCAPE N'\' OR [f].[Extension] LIKE @pattern17 ESCAPE N'\' OR [f].[Extension] LIKE @pattern19 ESCAPE N'\' OR [f].[Extension] LIKE @pattern21 ESCAPE N'\' OR [f].[Extension] LIKE @pattern23 ESCAPE N'\' OR [f].[Extension] LIKE @pattern25 ESCAPE N'\' OR [f].[Extension] LIKE @pattern27 ESCAPE N'\' OR [f].[Extension] LIKE @pattern29 ESCAPE N'\' OR [f].[Extension] LIKE @pattern31 ESCAPE N'\' OR [f].[Extension] LIKE @pattern33 ESCAPE N'\' OR [f].[Extension] LIKE @pattern35 ESCAPE N'\' OR [f].[Extension] LIKE @pattern37 ESCAPE N'\') AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
    ORDER BY [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## q=type:other
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE [f].[Extension] NOT LIKE @pattern ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern3 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern5 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern7 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern9 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern11 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern13 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern15 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern17 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern19 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern21 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern23 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern25 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern27 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern29 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern31 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern33 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern35 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern37 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern39 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern41 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern43 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern45 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern47 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern49 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern51 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern53 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern55 ESCAPE N'\' AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE [f].[Extension] NOT LIKE @pattern ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern3 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern5 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern7 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern9 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern11 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern13 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern15 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern17 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern19 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern21 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern23 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern25 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern27 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern29 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern31 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern33 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern35 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern37 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern39 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern41 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern43 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern45 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern47 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern49 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern51 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern53 ESCAPE N'\' AND [f].[Extension] NOT LIKE @pattern55 ESCAPE N'\' AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
    ORDER BY [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## q=t:Cat
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [t].[TagId], [t].[LastSearchedAt], [t].[LastViewedAt]
FROM [SFE].[TagActivity] AS [t]
WHERE [t].[TagId] = @ids1
;
SELECT [t].[Id]
FROM [SFE].[TagDefinitions] AS [t]
WHERE [t].[Id] = @ids1
;
SET IMPLICIT_TRANSACTIONS OFF;
SET NOCOUNT ON;
INSERT INTO [SFE].[TagActivity] ([TagId], [LastSearchedAt], [LastViewedAt])
VALUES (@p0, @p1, @p2);
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @ids1) AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f1]
        WHERE [f].[Id] = [f1].[FileId] AND [f1].[TagId] = @hiddenIds1)
) AS [f2]
;
SELECT [f3].[FullName], [f3].[Name], [f3].[Extension], [f3].[Size], [f3].[LastWriteTime], [f3].[Width], [f3].[Height], [f3].[Duration], [f3].[Pixels], [f3].[Id], [f2].[TagId], [f2].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @ids1) AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f1]
        WHERE [f].[Id] = [f1].[FileId] AND [f1].[TagId] = @hiddenIds1)
    ORDER BY [f].[FullName]
) AS [f3]
LEFT JOIN [SFE].[FileTags] AS [f2] ON [f3].[Id] = [f2].[FileId]
ORDER BY [f3].[FullName], [f3].[Id], [f2].[FileId]
;

## q=-t:Cat t:Vacation
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [t].[TagId], [t].[LastSearchedAt], [t].[LastViewedAt]
FROM [SFE].[TagActivity] AS [t]
WHERE [t].[TagId] = @ids1
;
SELECT [t].[Id]
FROM [SFE].[TagDefinitions] AS [t]
WHERE [t].[Id] = @ids1
;
SET IMPLICIT_TRANSACTIONS OFF;
SET NOCOUNT ON;
INSERT INTO [SFE].[TagActivity] ([TagId], [LastSearchedAt], [LastViewedAt])
VALUES (@p0, @p1, @p2);
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @ids2) AND EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f1]
        WHERE [f].[Id] = [f1].[FileId] AND [f1].[TagId] = @ids11) AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f2]
        WHERE [f].[Id] = [f2].[FileId] AND [f2].[TagId] = @hiddenIds1)
) AS [f3]
;
SELECT [f4].[FullName], [f4].[Name], [f4].[Extension], [f4].[Size], [f4].[LastWriteTime], [f4].[Width], [f4].[Height], [f4].[Duration], [f4].[Pixels], [f4].[Id], [f3].[TagId], [f3].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @ids2) AND EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f1]
        WHERE [f].[Id] = [f1].[FileId] AND [f1].[TagId] = @ids11) AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f2]
        WHERE [f].[Id] = [f2].[FileId] AND [f2].[TagId] = @hiddenIds1)
    ORDER BY [f].[FullName]
) AS [f4]
LEFT JOIN [SFE].[FileTags] AS [f3] ON [f4].[Id] = [f3].[FileId]
ORDER BY [f4].[FullName], [f4].[Id], [f3].[FileId]
;

## q=special:untagged
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId]) AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f1]
        WHERE [f].[Id] = [f1].[FileId] AND [f1].[TagId] = @hiddenIds1)
) AS [f2]
;
SELECT [f3].[FullName], [f3].[Name], [f3].[Extension], [f3].[Size], [f3].[LastWriteTime], [f3].[Width], [f3].[Height], [f3].[Duration], [f3].[Pixels], [f3].[Id], [f2].[TagId], [f2].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId]) AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f1]
        WHERE [f].[Id] = [f1].[FileId] AND [f1].[TagId] = @hiddenIds1)
    ORDER BY [f].[FullName]
) AS [f3]
LEFT JOIN [SFE].[FileTags] AS [f2] ON [f3].[Id] = [f2].[FileId]
ORDER BY [f3].[FullName], [f3].[Id], [f2].[FileId]
;

## q=t:Archived
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [t].[TagId], [t].[LastSearchedAt], [t].[LastViewedAt]
FROM [SFE].[TagActivity] AS [t]
WHERE [t].[TagId] = @ids1
;
SELECT [t].[Id]
FROM [SFE].[TagDefinitions] AS [t]
WHERE [t].[Id] = @ids1
;
SET IMPLICIT_TRANSACTIONS OFF;
SET NOCOUNT ON;
INSERT INTO [SFE].[TagActivity] ([TagId], [LastSearchedAt], [LastViewedAt])
VALUES (@p0, @p1, @p2);
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @ids1) AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @ids1) AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix)
    ORDER BY [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## in Media
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE [f].[Name] LIKE @pattern ESCAPE N'\' AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE [f].[Name] LIKE @pattern ESCAPE N'\' AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
    ORDER BY [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## folder rules
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE [f].[Name] LIKE @pattern ESCAPE N'\' AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ((([f].[FullName] LIKE @selfPattern11 ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern13 ESCAPE N'\') AND ([f].[FullName] = @self14 OR SUBSTRING([f].[FullName], 0 + 1, @length15) = @prefix16)) OR (([f].[FullName] LIKE @selfPattern18 ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern20 ESCAPE N'\') AND ([f].[FullName] = @self21 OR SUBSTRING([f].[FullName], 0 + 1, @length22) = @prefix23) AND (([f].[FullName] NOT LIKE @selfPattern25 ESCAPE N'\' AND [f].[FullName] NOT LIKE @nestedPattern27 ESCAPE N'\') OR ([f].[FullName] <> @self28 AND SUBSTRING([f].[FullName], 0 + 1, @length29) <> @prefix30))))
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE [f].[Name] LIKE @pattern ESCAPE N'\' AND ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ((([f].[FullName] LIKE @selfPattern11 ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern13 ESCAPE N'\') AND ([f].[FullName] = @self14 OR SUBSTRING([f].[FullName], 0 + 1, @length15) = @prefix16)) OR (([f].[FullName] LIKE @selfPattern18 ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern20 ESCAPE N'\') AND ([f].[FullName] = @self21 OR SUBSTRING([f].[FullName], 0 + 1, @length22) = @prefix23) AND (([f].[FullName] NOT LIKE @selfPattern25 ESCAPE N'\' AND [f].[FullName] NOT LIKE @nestedPattern27 ESCAPE N'\') OR ([f].[FullName] <> @self28 AND SUBSTRING([f].[FullName], 0 + 1, @length29) <> @prefix30))))
    ORDER BY [f].[Name], [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[Name], [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Name desc=False page=1
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
    ORDER BY [f].[Name], [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[Name], [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Name desc=False page=2
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ([f].[Name] > @text OR ([f].[Name] = @text AND [f].[FullName] > @path))
    ORDER BY [f].[Name], [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[Name], [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Name desc=False page=3
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ([f].[Name] > @text OR ([f].[Name] = @text AND [f].[FullName] > @path))
    ORDER BY [f].[Name], [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[Name], [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Name desc=True page=1
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
    ORDER BY [f].[Name] DESC, [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[Name] DESC, [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Name desc=True page=2
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ([f].[Name] < @text OR ([f].[Name] = @text AND [f].[FullName] > @path))
    ORDER BY [f].[Name] DESC, [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[Name] DESC, [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Name desc=True page=3
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ([f].[Name] < @text OR ([f].[Name] = @text AND [f].[FullName] > @path))
    ORDER BY [f].[Name] DESC, [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[Name] DESC, [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Modified desc=False page=1
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
    ORDER BY [f].[LastWriteTime], [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[LastWriteTime], [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Modified desc=False page=2
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ([f].[LastWriteTime] > @time OR ([f].[LastWriteTime] = @time AND [f].[FullName] > @path))
    ORDER BY [f].[LastWriteTime], [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[LastWriteTime], [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Modified desc=False page=3
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ([f].[LastWriteTime] > @time OR ([f].[LastWriteTime] = @time AND [f].[FullName] > @path))
    ORDER BY [f].[LastWriteTime], [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[LastWriteTime], [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Modified desc=True page=1
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
    ORDER BY [f].[LastWriteTime] DESC, [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[LastWriteTime] DESC, [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Modified desc=True page=2
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ([f].[LastWriteTime] < @time OR ([f].[LastWriteTime] = @time AND [f].[FullName] > @path))
    ORDER BY [f].[LastWriteTime] DESC, [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[LastWriteTime] DESC, [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Modified desc=True page=3
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ([f].[LastWriteTime] < @time OR ([f].[LastWriteTime] = @time AND [f].[FullName] > @path))
    ORDER BY [f].[LastWriteTime] DESC, [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[LastWriteTime] DESC, [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Size desc=False page=1
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
    ORDER BY [f].[Size], [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[Size], [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Size desc=False page=2
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ([f].[Size] > @number OR ([f].[Size] = @number AND [f].[FullName] > @path))
    ORDER BY [f].[Size], [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[Size], [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Size desc=False page=3
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ([f].[Size] > @number OR ([f].[Size] = @number AND [f].[FullName] > @path))
    ORDER BY [f].[Size], [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[Size], [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Size desc=True page=1
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
    ORDER BY [f].[Size] DESC, [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[Size] DESC, [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Size desc=True page=2
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ([f].[Size] < @number OR ([f].[Size] = @number AND [f].[FullName] > @path))
    ORDER BY [f].[Size] DESC, [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[Size] DESC, [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Size desc=True page=3
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ([f].[Size] < @number OR ([f].[Size] = @number AND [f].[FullName] > @path))
    ORDER BY [f].[Size] DESC, [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[Size] DESC, [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Type desc=False page=1
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
    ORDER BY [f].[Extension], [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[Extension], [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Type desc=False page=2
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ([f].[Extension] > @text OR ([f].[Extension] = @text AND [f].[FullName] > @path))
    ORDER BY [f].[Extension], [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[Extension], [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Type desc=False page=3
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ([f].[Extension] > @text OR ([f].[Extension] = @text AND [f].[FullName] > @path))
    ORDER BY [f].[Extension], [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[Extension], [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Type desc=True page=1
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
    ORDER BY [f].[Extension] DESC, [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[Extension] DESC, [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Type desc=True page=2
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ([f].[Extension] < @text OR ([f].[Extension] = @text AND [f].[FullName] > @path))
    ORDER BY [f].[Extension] DESC, [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[Extension] DESC, [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Type desc=True page=3
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ([f].[Extension] < @text OR ([f].[Extension] = @text AND [f].[FullName] > @path))
    ORDER BY [f].[Extension] DESC, [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[Extension] DESC, [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Path desc=False page=1
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
    ORDER BY [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Path desc=False page=2
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND [f].[FullName] > @path
    ORDER BY [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Path desc=False page=3
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND [f].[FullName] > @path
    ORDER BY [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Path desc=True page=1
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
    ORDER BY [f].[FullName] DESC
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName] DESC, [f2].[Id], [f1].[FileId]
;

## sort=Path desc=True page=2
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND [f].[FullName] < @path
    ORDER BY [f].[FullName] DESC
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName] DESC, [f2].[Id], [f1].[FileId]
;

## sort=Path desc=True page=3
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND [f].[FullName] < @path
    ORDER BY [f].[FullName] DESC
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName] DESC, [f2].[Id], [f1].[FileId]
;

## sort=Duration desc=False page=1
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND [f].[Duration] > 0.0E0
    ORDER BY [f].[Duration], [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[Duration], [f2].[FullName], [f2].[Id], [f1].[FileId]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ([f].[Duration] IS NULL OR [f].[Duration] <= 0.0E0)
    ORDER BY [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Duration desc=False page=2
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND [f].[Duration] > 0.0E0 AND ([f].[Duration] > @seconds OR ([f].[Duration] = @seconds AND [f].[FullName] > @path))
    ORDER BY [f].[Duration], [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[Duration], [f2].[FullName], [f2].[Id], [f1].[FileId]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ([f].[Duration] IS NULL OR [f].[Duration] <= 0.0E0)
    ORDER BY [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Duration desc=False page=3
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ([f].[Duration] IS NULL OR [f].[Duration] <= 0.0E0) AND [f].[FullName] > @path
    ORDER BY [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Duration desc=True page=1
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND [f].[Duration] > 0.0E0
    ORDER BY [f].[Duration] DESC, [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[Duration] DESC, [f2].[FullName], [f2].[Id], [f1].[FileId]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ([f].[Duration] IS NULL OR [f].[Duration] <= 0.0E0)
    ORDER BY [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Duration desc=True page=2
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND [f].[Duration] > 0.0E0 AND ([f].[Duration] < @seconds OR ([f].[Duration] = @seconds AND [f].[FullName] > @path))
    ORDER BY [f].[Duration] DESC, [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[Duration] DESC, [f2].[FullName], [f2].[Id], [f1].[FileId]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ([f].[Duration] IS NULL OR [f].[Duration] <= 0.0E0)
    ORDER BY [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Duration desc=True page=3
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ([f].[Duration] IS NULL OR [f].[Duration] <= 0.0E0) AND [f].[FullName] > @path
    ORDER BY [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Resolution desc=False page=1
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND [f].[Pixels] > CAST(0 AS bigint)
    ORDER BY [f].[Pixels], [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[Pixels], [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Resolution desc=False page=2
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND [f].[Pixels] > CAST(0 AS bigint) AND ([f].[Pixels] > @number OR ([f].[Pixels] = @number AND [f].[FullName] > @path))
    ORDER BY [f].[Pixels], [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[Pixels], [f2].[FullName], [f2].[Id], [f1].[FileId]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ([f].[Pixels] IS NULL OR [f].[Pixels] <= CAST(0 AS bigint))
    ORDER BY [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Resolution desc=False page=3
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ([f].[Pixels] IS NULL OR [f].[Pixels] <= CAST(0 AS bigint)) AND [f].[FullName] > @path
    ORDER BY [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Resolution desc=True page=1
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT COUNT(*)
FROM (
    SELECT TOP(@p) 1 AS empty
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1)
) AS [f1]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND [f].[Pixels] > CAST(0 AS bigint)
    ORDER BY [f].[Pixels] DESC, [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[Pixels] DESC, [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Resolution desc=True page=2
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND [f].[Pixels] > CAST(0 AS bigint) AND ([f].[Pixels] < @number OR ([f].[Pixels] = @number AND [f].[FullName] > @path))
    ORDER BY [f].[Pixels] DESC, [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[Pixels] DESC, [f2].[FullName], [f2].[Id], [f1].[FileId]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ([f].[Pixels] IS NULL OR [f].[Pixels] <= CAST(0 AS bigint))
    ORDER BY [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName], [f2].[Id], [f1].[FileId]
;

## sort=Resolution desc=True page=3
SELECT [t].[Id], [t].[Name], [t].[Shorthand], [t].[Color], [t].[IsCategory], [t].[IsHidden], [t].[DisambiguationId]
FROM [SFE].[TagDefinitions] AS [t]
;
SELECT [t].[TagId], [t].[Name]
FROM [SFE].[TagAliases] AS [t]
;
SELECT [t].[ParentId], [t].[ChildId]
FROM [SFE].[TagParents] AS [t]
;
SELECT [f2].[FullName], [f2].[Name], [f2].[Extension], [f2].[Size], [f2].[LastWriteTime], [f2].[Width], [f2].[Height], [f2].[Duration], [f2].[Pixels], [f2].[Id], [f1].[TagId], [f1].[FileId]
FROM (
    SELECT TOP(@p) [f].[FullName], [f].[Name], [f].[Extension], [f].[Size], [f].[LastWriteTime], [f].[Width], [f].[Height], [f].[Duration], [f].[Pixels], [f].[Id]
    FROM [SFE].[Files] AS [f]
    WHERE ([f].[FullName] LIKE @selfPattern ESCAPE N'\' OR [f].[FullName] LIKE @nestedPattern ESCAPE N'\') AND ([f].[FullName] = @self OR SUBSTRING([f].[FullName], 0 + 1, @length) = @prefix) AND NOT EXISTS (
        SELECT 1
        FROM [SFE].[FileTags] AS [f0]
        WHERE [f].[Id] = [f0].[FileId] AND [f0].[TagId] = @hiddenIds1) AND ([f].[Pixels] IS NULL OR [f].[Pixels] <= CAST(0 AS bigint)) AND [f].[FullName] > @path
    ORDER BY [f].[FullName]
) AS [f2]
LEFT JOIN [SFE].[FileTags] AS [f1] ON [f2].[Id] = [f1].[FileId]
ORDER BY [f2].[FullName], [f2].[Id], [f1].[FileId]
;

