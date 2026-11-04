import json
import sys

# Prints the manifest entries whose file contains one of the given substrings.
manifest = json.load(open(sys.argv[1]))
documents = manifest['documents'] if isinstance(manifest, dict) else manifest
for document in documents:
    if any(key in document['file'] for key in sys.argv[2:]):
        print(json.dumps(document, indent=1)[:3000])
