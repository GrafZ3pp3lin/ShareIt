-- PostgreSQL schema for content packages and contents

-- Table for content packages. Nothing names a package: item names and MIME
-- types live in the client-sealed per-item `meta` column. Every package
-- expires: abandoned uploads are swept by the cleanup service without any
-- draft/ready state machine.
CREATE TABLE IF NOT EXISTS content_packages (
    id UUID PRIMARY KEY,
    created_at TIMESTAMPTZ NOT NULL,
    user_id UUID,
    max_access_count INT,
    current_access_count INT NOT NULL DEFAULT 0,
    last_access_at TIMESTAMPTZ,
    expires_at TIMESTAMPTZ NOT NULL,
    unlock_verifier TEXT,
    CONSTRAINT chk_access_count CHECK (current_access_count >= 0),
    CONSTRAINT chk_max_access_count CHECK (max_access_count IS NULL OR max_access_count > 0)
);

-- Indexes for better query performance
CREATE INDEX IF NOT EXISTS idx_packages_user_id ON content_packages(user_id);
CREATE INDEX IF NOT EXISTS idx_packages_expires_at ON content_packages(expires_at);
CREATE INDEX IF NOT EXISTS idx_packages_access_limit ON content_packages(current_access_count, max_access_count)
    WHERE max_access_count IS NOT NULL;

-- Keyset pagination for the owner's links list: newest first.
CREATE INDEX IF NOT EXISTS idx_packages_user_created
    ON content_packages(user_id, created_at DESC, id DESC)
    WHERE user_id IS NOT NULL;

-- Index for cleanup queries (finding expired or limit-reached packages).
CREATE INDEX IF NOT EXISTS idx_packages_for_cleanup ON content_packages(expires_at, current_access_count, max_access_count);

-- Restricting the index to exhausted packages keeps it small and lets the
-- retention predicate use an index range scan.
CREATE INDEX IF NOT EXISTS idx_packages_exhausted_cleanup
    ON content_packages (last_access_at)
    WHERE max_access_count IS NOT NULL
      AND current_access_count >= max_access_count;


-- Table for external content metadata
CREATE TABLE IF NOT EXISTS contents (
    id UUID PRIMARY KEY,
    package_id UUID NOT NULL,
    content_type VARCHAR(50) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    size BIGINT NOT NULL,
    user_id UUID,
    meta TEXT,
    url TEXT NOT NULL,
    FOREIGN KEY (package_id) REFERENCES content_packages(id) ON DELETE CASCADE,
    CONSTRAINT chk_content_size CHECK (size >= 0)
);

-- Indexes for contents
CREATE INDEX IF NOT EXISTS idx_contents_user_id ON contents(user_id);
CREATE INDEX IF NOT EXISTS idx_contents_created_at ON contents(created_at);
CREATE INDEX IF NOT EXISTS idx_contents_content_type ON contents(content_type);
CREATE INDEX IF NOT EXISTS idx_contents_package_id ON contents(package_id);

-- Object keys are recorded before their content metadata disappears. S3
-- deletion happens afterward and is retried until the object is gone.
CREATE TABLE IF NOT EXISTS blob_deletion_jobs (
    blob_id TEXT PRIMARY KEY,
    created_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_blob_deletion_jobs_created_at ON blob_deletion_jobs(created_at);


-- Access sessions are created atomically with the package access claim. Only
-- the SHA-256 token hash is stored; the token itself is returned to the client
-- once and never written to the database or logs.
CREATE TABLE IF NOT EXISTS access_sessions (
    id UUID PRIMARY KEY,
    package_id UUID NOT NULL,
    client_id UUID NOT NULL,
    is_guest BOOLEAN NOT NULL,
    token_hash TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL,
    FOREIGN KEY (package_id) REFERENCES content_packages(id) ON DELETE CASCADE,
    CONSTRAINT uq_access_session_token_hash UNIQUE (token_hash)
);

CREATE INDEX IF NOT EXISTS idx_access_sessions_package_id ON access_sessions(package_id);
CREATE INDEX IF NOT EXISTS idx_access_sessions_expires_at ON access_sessions(expires_at);
