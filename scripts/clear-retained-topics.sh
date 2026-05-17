#!/usr/bin/env bash
#
# clear-retained-topics.sh
#
# Clears all retained MQTT messages that the MQTT Topics Translator may have
# previously published (back when the worker used .WithRetainFlag()).
#
# The list of destination topics is derived from the production Ansible
# inventory file:
#   homelabinfracode/configs.private/envprod/inventory/group_vars/incus_scope/
#       apps-mqtttranslator-configuration.yaml
#
# To clear a retained message on an MQTT broker, publish an EMPTY payload
# with the retain flag set on the same topic. This is what `-r -n` does in
# mosquitto_pub.
#
# Usage:
#   ./clear-retained-topics.sh -h <broker-host> -u <user> -P <password> \
#                              [-p <port>] [-n] [--cafile <path>] [--tls]
#
# Options:
#   -h HOST       MQTT broker hostname or IP        (required)
#   -p PORT       MQTT broker port                  (default: 1883)
#   -u USER       MQTT username                     (required)
#   -P PASS       MQTT password                     (required; or use -W to prompt)
#   -W            Prompt for the password instead of passing it on the cmd line
#   -n            Dry-run: print the mosquitto_pub commands but do not execute
#   --tls         Use TLS (port defaults to 8883 unless -p given)
#   --cafile F    Path to CA certificate file (implies --tls)
#   --help        Show this help
#
# Example:
#   ./clear-retained-topics.sh -h mqtt.mszlocal -u mszadmin -W
#
set -euo pipefail

# Destination topics published by the translator (from the prod inventory).
# Keep this list in sync with `mqtttranslator_mapping.translations[*].destinationTopics`.
TOPICS=(
    "pumplight/cmnd/POWER1"
    "pumplight/cmnd/POWER2"
    "redoxph/cmnd/POWER1"
    "redoxph/cmnd/POWER2"
)

HOST=""
PORT=""
USER=""
PASS=""
PROMPT_PASS=0
DRY_RUN=0
USE_TLS=0
CAFILE=""

print_help() {
    sed -n '2,30p' "$0"
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        -h) HOST="$2"; shift 2 ;;
        -p) PORT="$2"; shift 2 ;;
        -u) USER="$2"; shift 2 ;;
        -P) PASS="$2"; shift 2 ;;
        -W) PROMPT_PASS=1; shift ;;
        -n) DRY_RUN=1; shift ;;
        --tls) USE_TLS=1; shift ;;
        --cafile) CAFILE="$2"; USE_TLS=1; shift 2 ;;
        --help) print_help; exit 0 ;;
        *) echo "Unknown argument: $1" >&2; print_help; exit 2 ;;
    esac
done

if ! command -v mosquitto_pub >/dev/null 2>&1; then
    echo "ERROR: mosquitto_pub not found. Install the mosquitto-clients package." >&2
    exit 1
fi

if [[ -z "$HOST" || -z "$USER" ]]; then
    echo "ERROR: -h HOST and -u USER are required." >&2
    print_help
    exit 2
fi

if [[ "$PROMPT_PASS" -eq 1 ]]; then
    read -r -s -p "MQTT password for ${USER}@${HOST}: " PASS
    echo
fi

if [[ -z "$PASS" ]]; then
    echo "ERROR: password not provided (use -P or -W)." >&2
    exit 2
fi

if [[ -z "$PORT" ]]; then
    if [[ "$USE_TLS" -eq 1 ]]; then PORT=8883; else PORT=1883; fi
fi

TLS_ARGS=()
if [[ "$USE_TLS" -eq 1 ]]; then
    if [[ -n "$CAFILE" ]]; then
        TLS_ARGS+=(--cafile "$CAFILE")
    else
        TLS_ARGS+=(--capath /etc/ssl/certs)
    fi
fi

echo "Broker  : ${HOST}:${PORT}$([[ $USE_TLS -eq 1 ]] && echo ' (TLS)')"
echo "User    : ${USER}"
echo "Topics  : ${#TOPICS[@]}"
[[ "$DRY_RUN" -eq 1 ]] && echo "Mode    : DRY RUN (no messages will be published)"
echo

rc=0
for topic in "${TOPICS[@]}"; do
    # -r retain, -n send a null (zero-length) message -> clears retained value
    cmd=(mosquitto_pub
         -h "$HOST" -p "$PORT"
         -u "$USER" -P "$PASS"
         "${TLS_ARGS[@]}"
         -q 1
         -t "$topic"
         -r -n)

    if [[ "$DRY_RUN" -eq 1 ]]; then
        # Print the command with the password value masked (exact-match only).
        printf 'DRY: '
        for arg in "${cmd[@]}"; do
            if [[ "$arg" == "$PASS" ]]; then
                printf '%q ' '********'
            else
                printf '%q ' "$arg"
            fi
        done
        printf '\n'
        continue
    fi

    printf 'Clearing retained message on: %s ... ' "$topic"
    if "${cmd[@]}"; then
        echo "OK"
    else
        echo "FAILED"
        rc=1
    fi
done

exit "$rc"
