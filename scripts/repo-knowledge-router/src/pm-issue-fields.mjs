export function getPmIssueNumber(issue) {
  if (issue == null) return null;
  const n = issue.issue_number ?? issue.github_number ?? issue.gitee_number;
  if (n == null || n === "") return null;
  return n;
}
