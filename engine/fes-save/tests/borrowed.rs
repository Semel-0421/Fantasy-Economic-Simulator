use fes_save::{read_save, read_save_borrowed, write_save, RawSection, SaveHeader};

#[test]
fn borrowed_sections_alias_input_and_match_owned_reader() {
    let header = SaveHeader {
        format_version: 1,
        data_hash: 2,
        seed: 3,
        turn: 4,
    };
    let sections = vec![RawSection {
        key: "test".into(),
        version: 7,
        bytes: vec![9; 1024],
    }];
    let bytes = write_save(&header, &sections);
    let (got, borrowed) = read_save_borrowed(&bytes).unwrap();
    assert_eq!(got, header);
    assert_eq!(borrowed[0].key, sections[0].key);
    assert_eq!(borrowed[0].version, sections[0].version);
    assert_eq!(borrowed[0].bytes, sections[0].bytes);
    assert_eq!(
        borrowed[0].bytes.as_ptr(),
        bytes[bytes.len() - 1024..].as_ptr()
    );

    // Both entry points must reject the same malformed container, before loading a world.
    for end in 0..bytes.len() {
        assert_eq!(
            read_save(&bytes[..end]).unwrap_err(),
            read_save_borrowed(&bytes[..end]).unwrap_err()
        );
    }
    for offset in [0, 4, 28, 41] {
        let mut bad = bytes.clone();
        bad[offset..offset + 4].copy_from_slice(&(-1i32).to_le_bytes());
        assert_eq!(
            read_save(&bad).unwrap_err(),
            read_save_borrowed(&bad).unwrap_err()
        );
    }
}
