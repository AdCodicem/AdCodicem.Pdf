export default {
  extends: ["@commitlint/config-conventional"],
  // Dependabot writes its subjects as "Bump <dependency> from <a> to <b>", capitalized, which subject-case
  // refuses: Conventional commits failed on 18 of its first 19 pull requests (#196). Its prefix is the one
  // .github/dependabot.yml gives it, and semantic-release reads the type, not the case, so its own subjects are
  // let through rather than every capitalized subject.
  ignores: [(message) => /^build(\(deps(-dev)?\))?: Bump /.test(message)],
};
