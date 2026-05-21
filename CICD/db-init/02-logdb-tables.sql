-- LogDb 테이블 정의 (Dapper 기반이라 EF migration 으로 관리되지 않음).
-- LogManager.cs / LogDTO_Login.cs / LogDTO_Reward.cs 와 컬럼 일치.
-- 신규 볼륨에서만 1회 실행. 기존 환경에 적용하려면 수동으로 SOURCE 해야 함.

USE LogDb;

CREATE TABLE IF NOT EXISTS log_login (
    Id          BIGINT       AUTO_INCREMENT PRIMARY KEY,
    PlayerDbId  INT          NOT NULL,
    IsLogin     TINYINT(1)   NOT NULL,
    IpAddress   VARCHAR(45)  NULL,                        -- IPv6 max length
    Timestamp   DATETIME     NOT NULL,
    INDEX idx_player_ts (PlayerDbId, Timestamp),
    INDEX idx_ts (Timestamp)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS log_reward (
    Id          BIGINT       AUTO_INCREMENT PRIMARY KEY,
    PlayerDbId  INT          NOT NULL,
    ItemId      INT          NOT NULL,
    Count       INT          NOT NULL,
    Reason      VARCHAR(64)  NULL,
    Timestamp   DATETIME     NOT NULL,
    INDEX idx_player_ts (PlayerDbId, Timestamp),
    INDEX idx_item (ItemId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
