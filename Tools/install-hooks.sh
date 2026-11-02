#!/bin/sh
# Git does not carry hooks with a clone; run this once per checkout.
cd "$(git rev-parse --show-toplevel)" || exit 1
printf '#!/bin/sh\nexec sh Tools/pre-commit.sh\n' > "$(git rev-parse --git-path hooks)/pre-commit"
echo "pre-commit installed."
