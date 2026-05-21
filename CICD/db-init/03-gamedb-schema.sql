-- GameDb 스키마 (EF Core 마이그레이션: 20250905093401_InitialCreate)
-- 'dotnet ef migrations script --idempotent' 산출물. 신규 볼륨에서만 1회 실행.
USE GameDb;

CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
    `MigrationId` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
    `ProductVersion` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    CONSTRAINT `PK___EFMigrationsHistory` PRIMARY KEY (`MigrationId`)
) CHARACTER SET=utf8mb4;

START TRANSACTION;
DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20250905093401_InitialCreate') THEN

    ALTER DATABASE CHARACTER SET utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20250905093401_InitialCreate') THEN

    CREATE TABLE `Account` (
        `AccountDbId` int NOT NULL AUTO_INCREMENT,
        `AccountName` varchar(255) CHARACTER SET utf8mb4 NULL,
        CONSTRAINT `PK_Account` PRIMARY KEY (`AccountDbId`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20250905093401_InitialCreate') THEN

    CREATE TABLE `Player` (
        `PlayerDbId` int NOT NULL AUTO_INCREMENT,
        `PlayerName` varchar(255) CHARACTER SET utf8mb4 NULL,
        `AccountDbId` int NOT NULL,
        `Level` int NOT NULL,
        `Hp` int NOT NULL,
        `MaxHp` int NOT NULL,
        `Attack` int NOT NULL,
        `Speed` float NOT NULL,
        `TotalExp` int NOT NULL,
        CONSTRAINT `PK_Player` PRIMARY KEY (`PlayerDbId`),
        CONSTRAINT `FK_Player_Account_AccountDbId` FOREIGN KEY (`AccountDbId`) REFERENCES `Account` (`AccountDbId`) ON DELETE CASCADE
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20250905093401_InitialCreate') THEN

    CREATE TABLE `Item` (
        `ItemDbId` int NOT NULL AUTO_INCREMENT,
        `TemplateId` int NOT NULL,
        `Count` int NOT NULL,
        `Slot` int NOT NULL,
        `Equipped` tinyint(1) NOT NULL,
        `OwnerDbId` int NULL,
        CONSTRAINT `PK_Item` PRIMARY KEY (`ItemDbId`),
        CONSTRAINT `FK_Item_Player_OwnerDbId` FOREIGN KEY (`OwnerDbId`) REFERENCES `Player` (`PlayerDbId`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20250905093401_InitialCreate') THEN

    CREATE UNIQUE INDEX `IX_Account_AccountName` ON `Account` (`AccountName`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20250905093401_InitialCreate') THEN

    CREATE INDEX `IX_Item_OwnerDbId` ON `Item` (`OwnerDbId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20250905093401_InitialCreate') THEN

    CREATE INDEX `IX_Player_AccountDbId` ON `Player` (`AccountDbId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20250905093401_InitialCreate') THEN

    CREATE UNIQUE INDEX `IX_Player_PlayerName` ON `Player` (`PlayerName`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20250905093401_InitialCreate') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20250905093401_InitialCreate', '9.0.8');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

