-- V31: Add BlogPostComments table in accounts schema

CREATE TABLE IF NOT EXISTS accounts."BlogPostComments" (
    "Id" INT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "BlogPostId" INT NOT NULL,
    "UserId" VARCHAR(450) NOT NULL,
    "UserName" VARCHAR(256) NOT NULL,
    "Content" TEXT NOT NULL,
    "ParentCommentId" INT NULL,
    "CreatedAt" TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "LikesCount" INT NOT NULL DEFAULT 0,
    CONSTRAINT "FK_BlogPostComments_BlogPosts" FOREIGN KEY ("BlogPostId") REFERENCES accounts."BlogPosts" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_BlogPostComments_ParentComment" FOREIGN KEY ("ParentCommentId") REFERENCES accounts."BlogPostComments" ("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_BlogPostComments_BlogPostId" ON accounts."BlogPostComments" ("BlogPostId");
CREATE INDEX IF NOT EXISTS "IX_BlogPostComments_ParentCommentId" ON accounts."BlogPostComments" ("ParentCommentId");
