#!/bin/bash
set -e

NAME="$1"
shift 1 2>/dev/null || true

if [ -z "$NAME" ]; then
  echo "Usage: ralph-tree <worktree-name> [--tool amp|claude] [max_iterations]"
  exit 1
fi

WORKTREE_PATH="$(pwd)/.claude/worktrees/$NAME"

if [ ! -d "$WORKTREE_PATH" ]; then
  echo "Error: Worktree not found: $WORKTREE_PATH"
  echo "Available worktrees:"
  ls -1 "$(pwd)/.claude/worktrees/" 2>/dev/null || echo "  (none)"
  exit 1
fi

RALPH_SCRIPT="$WORKTREE_PATH/scripts/ralph/ralph.sh"

if [ ! -f "$RALPH_SCRIPT" ]; then
  echo "Error: ralph.sh not found at $RALPH_SCRIPT"
  exit 1
fi

PRD_FILE="$WORKTREE_PATH/scripts/ralph/prd.json"
if [ -f "$PRD_FILE" ]; then
  PRD_NAME=$(jq -r '.project // .name // "unknown"' "$PRD_FILE" 2>/dev/null)
  PRD_BRANCH=$(jq -r '.branchName // "unknown"' "$PRD_FILE" 2>/dev/null)
  TOTAL=$(jq '[.userStories[]] | length' "$PRD_FILE" 2>/dev/null || echo "?")
  DONE=$(jq '[.userStories[] | select(.passes == true)] | length' "$PRD_FILE" 2>/dev/null || echo "?")
  GIT_BRANCH=$(git -C "$WORKTREE_PATH" branch --show-current 2>/dev/null || echo "unknown")
  echo "Worktree:  $NAME"
  echo "PRD:       $PRD_NAME"
  echo "Branch:    $PRD_BRANCH"
  echo "Git:       $GIT_BRANCH"
  echo "Stories:   $DONE/$TOTAL complete"
  echo "Path:      $WORKTREE_PATH"
  if [ "$PRD_BRANCH" != "unknown" ] && [ "$GIT_BRANCH" != "unknown" ] && [ "$PRD_BRANCH" != "$GIT_BRANCH" ]; then
    echo "Warning: PRD branch and git branch differ"
  fi
  echo ""
fi

cd "$WORKTREE_PATH" && bash "$RALPH_SCRIPT" "$@"
