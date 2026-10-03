# SemVer 2.0.0 precedence (https://semver.org/#spec-item-11) as a jq sort key, so that
# `max_by(semver_key)` and `sort_by(semver_key)` order versions the way NuGet does.
#
# Build metadata is ignored. A version without a prerelease ranks above every prerelease of
# the same version. Prerelease identifiers compare left to right: numeric ones numerically,
# alphanumeric ones in ASCII order, a numeric one below an alphanumeric one, and a longer
# list above a shorter one it starts with. NuGet compares labels without regard to case and
# its flat container lowercases them, so they are lowercased here. A fourth numeric part,
# which NuGet still accepts, ranks after the patch.
def semver_key:
  ascii_downcase
  | sub("\\+.*$"; "")
  | capture("^(?<core>[0-9]+(\\.[0-9]+){2,3})(-(?<pre>[0-9a-z.-]+))?$")
  | [ (.core | split(".") | map(tonumber) | . + [0] | .[0:4]),
      (if .pre == null then [1]
       else [0, (.pre | split(".") | map(if test("^[0-9]+$") then [0, tonumber] else [1, .] end))]
       end) ];
