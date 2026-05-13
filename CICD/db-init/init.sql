-- MariaDB 컨테이너 최초 기동 시 1회 실행.
-- 'DY' 유저는 compose 의 MARIADB_USER 로 이미 생성되며, MARIADB_DATABASE 로 GameDb 도 생성됨.
-- 여기선 나머지 DB 들을 만들고 같은 유저에게 권한 부여.

CREATE DATABASE IF NOT EXISTS LogDb    CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE DATABASE IF NOT EXISTS SharedDB CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE DATABASE IF NOT EXISTS AccountDB CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

GRANT ALL PRIVILEGES ON GameDb.*    TO 'DY'@'%';
GRANT ALL PRIVILEGES ON LogDb.*     TO 'DY'@'%';
GRANT ALL PRIVILEGES ON SharedDB.*  TO 'DY'@'%';
GRANT ALL PRIVILEGES ON AccountDB.* TO 'DY'@'%';
FLUSH PRIVILEGES;
